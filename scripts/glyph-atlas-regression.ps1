<#
.SYNOPSIS
    Runs the fixed local glyph-atlas regression lane.

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

Invoke-Diagnostic `
    "glyph-atlas-matrix-$laneLabel-$scaleLabel.txt" `
    (@("--diagnose-glyph-atlas-matrix", "$MatrixFrames") + $scaleArgs) `
    @("Expected matrix:", "matrix.expected", "Degradation contract:", "Accepted degradation:", "Final:", "Glyph atlas:", "matrix.actual")

Invoke-Diagnostic `
    "glyph-atlas-soak-$laneLabel-$scaleLabel.txt" `
    (@("--diagnose-glyph-atlas-soak", "$SoakFrames", "--pressure-every", "$PressureEvery") + $scaleArgs) `
    @("Page policy:", "Final:", "Soak summary:", "Soak thresholds:", "soak.actual", "Glyph atlas:")

Invoke-Diagnostic `
    "glyph-atlas-color-formats-$laneLabel-$scaleLabel.txt" `
    (New-ColorGlyphArgs) `
    @("Color glyph formats:", "Color glyph natural coverage:", "Probe:")

Invoke-Diagnostic `
    "glyph-atlas-bidi-oracle-$laneLabel-$scaleLabel.txt" `
    @("--diagnose-glyph-atlas-bidi-oracle") `
    @("bidi-oracle.expected", "BiDi oracle:", "bidi-oracle.actual", "Probe:")

Invoke-Diagnostic `
    "glyph-atlas-glyph-oracle-$laneLabel-$scaleLabel.txt" `
    @("--diagnose-glyph-atlas-glyph-oracle") `
    @("glyph-oracle.expected", "Glyph oracle:", "glyph-oracle.actual", "Probe:")

Write-Host "Glyph atlas regression lane complete: mode=$Mode matrixFrames=$MatrixFrames soakFrames=$SoakFrames pressureEvery=$PressureEvery." -ForegroundColor Green
