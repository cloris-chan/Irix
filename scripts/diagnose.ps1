<#
.SYNOPSIS
    Local diagnostic smoke test for Irix D3D12 rendering pipeline.
    Renders one frame with test rectangles + text, dumps cache stats, and exits.

.DESCRIPTION
    Verifies:
    - D3D12 device creation
    - Rectangle rendering (D3D12Renderer2D)
    - Text rendering (D3D12 GlyphAtlas with DirectWrite shaping/raster source)
    - Glyph atlas cache stats
    - Device removed state and error reason

    Requires: Windows with D3D12-capable GPU, .NET 10 SDK.
    Does NOT run in CI headless environments (no GPU).

.EXAMPLE
    .\scripts\diagnose.ps1
    dotnet run --project src\Irix.Poc -p:IrixDiagnostics=true -- --diagnose
#>

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$pocProject = Join-Path $repoRoot "src\Irix.Poc\Irix.Poc.csproj"

if (-not (Test-Path $pocProject)) {
    Write-Error "Irix.Poc project not found at: $pocProject"
    exit 1
}

Write-Host "=== Irix D3D12 Diagnostic Smoke Test ===" -ForegroundColor Cyan
Write-Host "Project: $pocProject"
Write-Host ""

dotnet run --project $pocProject -p:IrixDiagnostics=true -- --diagnose
$exitCode = $LASTEXITCODE

Write-Host ""
if ($exitCode -eq 0) {
    Write-Host "PASS: Diagnostic mode completed successfully." -ForegroundColor Green
} else {
    Write-Host "FAIL: Diagnostic mode exited with code $exitCode." -ForegroundColor Red
}

exit $exitCode
