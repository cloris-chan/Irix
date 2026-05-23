<#
.SYNOPSIS
    Runs the fixed local glyph-atlas regression smoke lane.

.EXAMPLE
    .\scripts\glyph-atlas-regression.ps1

.EXAMPLE
    .\scripts\glyph-atlas-regression.ps1 -MatrixFrames 3 -SoakFrames 24 -PressureEvery 4
#>

param(
    [int]$MatrixFrames = 3,

    [int]$SoakFrames = 24,

    [int]$PressureEvery = 4,

    [int]$ScalePercent = 0
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

function New-ScaleArgs {
    if ($ScalePercent -gt 0) {
        return @("--diagnose-scale", "$ScalePercent")
    }

    return @()
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

$scaleArgs = New-ScaleArgs
$scaleLabel = if ($ScalePercent -gt 0) { "$ScalePercent`pct" } else { "current-scale" }

Invoke-Diagnostic `
    "glyph-atlas-matrix-$scaleLabel.txt" `
    (@("--diagnose-glyph-atlas-matrix", "$MatrixFrames") + $scaleArgs) `
    @("Expected matrix:", "matrix.expected", "Degradation contract:", "Accepted degradation:", "Final:", "Glyph atlas:", "matrix.actual")

Invoke-Diagnostic `
    "glyph-atlas-soak-$scaleLabel.txt" `
    (@("--diagnose-glyph-atlas-soak", "$SoakFrames", "--pressure-every", "$PressureEvery") + $scaleArgs) `
    @("Page policy:", "Final:", "Soak summary:", "Soak thresholds:", "soak.actual", "Glyph atlas:")

Invoke-Diagnostic `
    "glyph-atlas-bidi-oracle-$scaleLabel.txt" `
    @("--diagnose-glyph-atlas-bidi-oracle") `
    @("BiDi oracle:", "Probe:")

Invoke-Diagnostic `
    "glyph-atlas-glyph-oracle-$scaleLabel.txt" `
    @("--diagnose-glyph-atlas-glyph-oracle") `
    @("Glyph oracle:", "Probe:")

Write-Host "Glyph atlas regression lane complete." -ForegroundColor Green
