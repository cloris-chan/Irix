<#
.SYNOPSIS
    Runs the fixed local glyph-atlas regression lane and validates its summary contract.

.EXAMPLE
    .\scripts\glyph-atlas-regression.ps1

.EXAMPLE
    .\scripts\glyph-atlas-regression.ps1 -Mode Local

.EXAMPLE
    .\scripts\glyph-atlas-regression.ps1 -Mode Nightly
#>

param(
    [ValidateSet("Smoke", "Local", "Nightly")]
    [string]$Mode = "Smoke",

    [int]$MatrixFrames = 0,

    [int]$SoakFrames = 0,

    [int]$PressureEvery = 0,

    [int]$ScalePercent = 0,

    [uint32]$ColorPixelsPerEm = 64,

    [string]$ColorFontFile = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$pocProject = Join-Path $repoRoot "src\Irix.Poc\Irix.Poc.csproj"
$resultsDir = Join-Path $repoRoot "TestResults"

if (-not (Test-Path $pocProject)) {
    throw "Irix.Poc project not found at: $pocProject"
}

if (-not (Test-Path $resultsDir)) {
    New-Item -ItemType Directory -Path $resultsDir | Out-Null
}

function Get-LaneDefaults([string]$LaneMode) {
    switch ($LaneMode) {
        "Local" {
            return [pscustomobject]@{
                MatrixFrames = 3
                SoakFrames = 300
                PressureEvery = 6
            }
        }
        "Nightly" {
            return [pscustomobject]@{
                MatrixFrames = 3
                SoakFrames = 900
                PressureEvery = 6
            }
        }
        default {
            return [pscustomobject]@{
                MatrixFrames = 3
                SoakFrames = 60
                PressureEvery = 6
            }
        }
    }
}

function New-ScaleArgs {
    if ($ScalePercent -gt 0) {
        return @("--diagnose-scale", "$ScalePercent")
    }

    return @()
}

function New-ColorGlyphArgs {
    $diagnosticArgs = @("--diagnose-glyph-atlas-color-formats", "$ColorPixelsPerEm")
    if (-not [string]::IsNullOrWhiteSpace($ColorFontFile)) {
        $diagnosticArgs += @("--diagnose-color-glyph-font-file", $ColorFontFile)
    }

    return $diagnosticArgs
}

function Invoke-Diagnostic([string]$Name, [string[]]$Arguments, [string[]]$SummaryPatterns) {
    $outputPath = Join-Path $resultsDir $Name
    $summaryPath = [System.IO.Path]::ChangeExtension($outputPath, ".summary.txt")
    $diagnosticArgs = @("run", "--project", $pocProject, "-c", "Release", "--") + @($Arguments) + @("--diagnostic-output", $outputPath)

    Write-Host "=== Running $Name ===" -ForegroundColor Cyan
    Remove-Item $outputPath, $summaryPath -Force -ErrorAction SilentlyContinue
    dotnet @diagnosticArgs | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path $outputPath)) {
        throw "$Name completed but did not create diagnostic output: $outputPath"
    }

    $summary = Get-Content -Encoding UTF8 $outputPath | Select-String -Pattern $SummaryPatterns
    $summaryLines = @($summary | ForEach-Object { $_.Line })
    $summaryLines | Set-Content -Encoding UTF8 $summaryPath
    $summaryLines | ForEach-Object { Write-Host $_ }
    Write-Host "Output: $outputPath"
    Write-Host "Summary: $summaryPath"
    Write-Host ""

    return [pscustomobject]@{
        Name = $Name
        OutputPath = $outputPath
        SummaryPath = $summaryPath
        Lines = $summaryLines
    }
}

function Get-RequiredSummaryLine([object]$Diagnostic, [string]$Prefix) {
    if (-not (Test-Path $Diagnostic.SummaryPath)) {
        throw "$($Diagnostic.Name) did not create summary file: $($Diagnostic.SummaryPath)"
    }

    if ($Diagnostic.Lines.Count -eq 0) {
        throw "$($Diagnostic.Name) summary is empty: $($Diagnostic.SummaryPath)"
    }

    $matches = @($Diagnostic.Lines | Where-Object { $_.StartsWith($Prefix, [System.StringComparison]::Ordinal) })
    if ($matches.Count -ne 1) {
        throw "$($Diagnostic.Name) summary expected exactly one '$Prefix' line but found $($matches.Count). Summary: $($Diagnostic.SummaryPath)"
    }

    return $matches[0]
}

function ConvertTo-MachineFields([string]$Line) {
    $fields = @{}
    foreach ($part in $Line.Split([char]' ', [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $separator = $part.IndexOf('=')
        if ($separator -le 0) {
            continue
        }

        $fields[$part.Substring(0, $separator)] = $part.Substring($separator + 1)
    }

    return $fields
}

function Assert-FieldEquals([hashtable]$Fields, [string]$FieldName, [string]$ExpectedValue, [string]$Context, [string]$Line) {
    if (-not $Fields.ContainsKey($FieldName)) {
        throw "$Context drift: missing field '$FieldName'. Line: $Line"
    }

    $actualValue = [string]$Fields[$FieldName]
    if ($actualValue -ne $ExpectedValue) {
        throw "$Context drift: field '$FieldName' expected '$ExpectedValue' but was '$actualValue'. Line: $Line"
    }
}

function Assert-FieldPositive([hashtable]$Fields, [string]$FieldName, [string]$Context, [string]$Line) {
    if (-not $Fields.ContainsKey($FieldName)) {
        throw "$Context drift: missing field '$FieldName'. Line: $Line"
    }

    [int]$actualValue = 0
    if (-not [int]::TryParse([string]$Fields[$FieldName], [ref]$actualValue) -or $actualValue -le 0) {
        throw "$Context drift: field '$FieldName' expected positive integer but was '$($Fields[$FieldName])'. Line: $Line"
    }
}

function Assert-FieldsMatch([hashtable]$ExpectedFields, [hashtable]$ActualFields, [string[]]$FieldNames, [string]$Context, [string]$ExpectedLine, [string]$ActualLine) {
    foreach ($fieldName in $FieldNames) {
        if (-not $ExpectedFields.ContainsKey($fieldName)) {
            throw "$Context drift: expected line missing field '$fieldName'. Line: $ExpectedLine"
        }

        if (-not $ActualFields.ContainsKey($fieldName)) {
            throw "$Context drift: actual line missing field '$fieldName'. Line: $ActualLine"
        }

        $expectedValue = [string]$ExpectedFields[$fieldName]
        $actualValue = [string]$ActualFields[$fieldName]
        if ($actualValue -ne $expectedValue) {
            throw "$Context drift: field '$fieldName' expected '$expectedValue' but was '$actualValue'. Expected: $ExpectedLine Actual: $ActualLine"
        }
    }
}

function Assert-LineContains([string]$Line, [string]$Token, [string]$Context) {
    if ($Line.IndexOf($Token, [System.StringComparison]::Ordinal) -lt 0) {
        throw "$Context drift: expected token '$Token'. Line: $Line"
    }
}

function Assert-RegressionSummaries([object]$Matrix, [object]$Soak, [object]$ColorFormats, [object]$BidiOracle, [object]$GlyphOracle, [string]$GuardSummaryPath) {
    $matrixExpectedLine = Get-RequiredSummaryLine $Matrix "matrix.expected"
    $matrixActualLine = Get-RequiredSummaryLine $Matrix "matrix.actual"
    $matrixExpectedFields = ConvertTo-MachineFields $matrixExpectedLine
    $matrixActualFields = ConvertTo-MachineFields $matrixActualLine
    Assert-FieldEquals $matrixExpectedFields "degradedRuns" "0" "matrix.expected" $matrixExpectedLine
    Assert-FieldEquals $matrixExpectedFields "overlayFallback" "False" "matrix.expected" $matrixExpectedLine
    Assert-FieldEquals $matrixActualFields "glyphAtlasInitialized" "True" "matrix.actual" $matrixActualLine
    Assert-FieldEquals $matrixActualFields "degradedRuns" "0" "matrix.actual" $matrixActualLine
    Assert-FieldEquals $matrixActualFields "overlaySync" "False" "matrix.actual" $matrixActualLine

    $soakActualLine = Get-RequiredSummaryLine $Soak "soak.actual"
    $soakGlyphLine = Get-RequiredSummaryLine $Soak "Glyph atlas:"
    $soakFields = ConvertTo-MachineFields $soakActualLine
    Assert-FieldEquals $soakFields "deviceLost" "False" "soak.actual" $soakActualLine
    Assert-FieldEquals $soakFields "overlaySync" "False" "soak.actual" $soakActualLine
    Assert-FieldEquals $soakFields "syncWaits" "0" "soak.actual" $soakActualLine
    Assert-FieldEquals $soakFields "hardFullWithoutReuse" "0" "soak.actual" $soakActualLine
    Assert-FieldEquals $soakFields "countersPresent" "True" "soak.actual" $soakActualLine
    Assert-LineContains $soakGlyphLine "RecordFailed=0" "soak glyph atlas"
    Assert-LineContains $soakGlyphLine "recordFailurePhase=None" "soak glyph atlas"

    $colorSummaryLine = Get-RequiredSummaryLine $ColorFormats "Color glyph formats:"
    $colorCoverageLine = Get-RequiredSummaryLine $ColorFormats "Color glyph natural coverage:"

    $bidiExpectedLine = Get-RequiredSummaryLine $BidiOracle "bidi-oracle.expected"
    $bidiActualLine = Get-RequiredSummaryLine $BidiOracle "bidi-oracle.actual"
    $bidiExpectedFields = ConvertTo-MachineFields $bidiExpectedLine
    $bidiActualFields = ConvertTo-MachineFields $bidiActualLine
    Assert-FieldsMatch $bidiExpectedFields $bidiActualFields @("probes", "labels", "layoutOracle", "pixelOracle", "overlayFallback") "bidi-oracle" $bidiExpectedLine $bidiActualLine
    Assert-FieldEquals $bidiActualFields "failedProbes" "0" "bidi-oracle.actual" $bidiActualLine

    $glyphExpectedLine = Get-RequiredSummaryLine $GlyphOracle "glyph-oracle.expected"
    $glyphActualLine = Get-RequiredSummaryLine $GlyphOracle "glyph-oracle.actual"
    $glyphExpectedFields = ConvertTo-MachineFields $glyphExpectedLine
    $glyphActualFields = ConvertTo-MachineFields $glyphActualLine
    Assert-FieldsMatch $glyphExpectedFields $glyphActualFields @("probes", "labels", "layoutOracle", "pixelOracle", "overlayFallback") "glyph-oracle" $glyphExpectedLine $glyphActualLine
    Assert-FieldEquals $glyphActualFields "failedProbes" "0" "glyph-oracle.actual" $glyphActualLine
    Assert-FieldPositive $glyphActualFields "totalGlyphs" "glyph-oracle.actual" $glyphActualLine

    $guardLines = @(
        "glyph-atlas-regression.guard status=Passed mode=$Mode matrixFrames=$MatrixFrames soakFrames=$SoakFrames pressureEvery=$PressureEvery",
        $matrixExpectedLine,
        $matrixActualLine,
        $soakActualLine,
        $colorSummaryLine,
        $colorCoverageLine,
        $bidiExpectedLine,
        $bidiActualLine,
        $glyphExpectedLine,
        $glyphActualLine
    )
    $guardLines | Set-Content -Encoding UTF8 $GuardSummaryPath
    Write-Host "Summary guard: passed"
    Write-Host "Guard summary: $GuardSummaryPath"
    Write-Host ""
}

$laneDefaults = Get-LaneDefaults $Mode
if ($MatrixFrames -le 0) { $MatrixFrames = $laneDefaults.MatrixFrames }
if ($SoakFrames -le 0) { $SoakFrames = $laneDefaults.SoakFrames }
if ($PressureEvery -le 0) { $PressureEvery = $laneDefaults.PressureEvery }

$scaleArgs = New-ScaleArgs
$scaleLabel = if ($ScalePercent -gt 0) { "$ScalePercent`pct" } else { "current-scale" }
$laneLabel = $Mode.Trim().ToLowerInvariant()

Write-Host "Glyph atlas regression lane: mode=$Mode matrixFrames=$MatrixFrames soakFrames=$SoakFrames pressureEvery=$PressureEvery colorPpem=$ColorPixelsPerEm scale=$scaleLabel" -ForegroundColor Cyan
Write-Host ""

$matrixDiagnostic = Invoke-Diagnostic `
    "glyph-atlas-matrix-$laneLabel-$scaleLabel.txt" `
    (@("--diagnose-glyph-atlas-matrix", "$MatrixFrames") + $scaleArgs) `
    @("Expected matrix:", "matrix.expected", "Degradation contract:", "Accepted degradation:", "Final:", "Glyph atlas:", "matrix.actual")

$soakDiagnostic = Invoke-Diagnostic `
    "glyph-atlas-soak-$laneLabel-$scaleLabel.txt" `
    (@("--diagnose-glyph-atlas-soak", "$SoakFrames", "--pressure-every", "$PressureEvery") + $scaleArgs) `
    @("Page policy:", "Final:", "Soak summary:", "Soak thresholds:", "soak.actual", "Glyph atlas:")

$colorFormatDiagnostic = Invoke-Diagnostic `
    "glyph-atlas-color-formats-$laneLabel-$scaleLabel.txt" `
    (New-ColorGlyphArgs) `
    @("Color glyph formats:", "Color glyph natural coverage:", "Probe:")

$bidiOracleDiagnostic = Invoke-Diagnostic `
    "glyph-atlas-bidi-oracle-$laneLabel-$scaleLabel.txt" `
    @("--diagnose-glyph-atlas-bidi-oracle") `
    @("bidi-oracle.expected", "BiDi oracle:", "bidi-oracle.actual", "Probe:")

$glyphOracleDiagnostic = Invoke-Diagnostic `
    "glyph-atlas-glyph-oracle-$laneLabel-$scaleLabel.txt" `
    @("--diagnose-glyph-atlas-glyph-oracle") `
    @("glyph-oracle.expected", "Glyph oracle:", "glyph-oracle.actual", "Probe:")

$guardSummaryPath = Join-Path $resultsDir "glyph-atlas-regression-$laneLabel-$scaleLabel.guard.summary.txt"
Assert-RegressionSummaries $matrixDiagnostic $soakDiagnostic $colorFormatDiagnostic $bidiOracleDiagnostic $glyphOracleDiagnostic $guardSummaryPath

Write-Host "Glyph atlas regression lane complete: mode=$Mode matrixFrames=$MatrixFrames soakFrames=$SoakFrames pressureEvery=$PressureEvery." -ForegroundColor Green
