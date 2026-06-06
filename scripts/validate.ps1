<#
.SYNOPSIS
    Local validation entrypoint for Irix.

.DESCRIPTION
    Runs the local validation lanes that currently stand in for the constrained
    GitHub Actions setup. Quick mirrors the lightweight CI lane. Focused runs the
    architecture/source-boundary, diagnostics, partial-apply, composition, and
    scroll/input preflight coverage. GlyphSmoke delegates to the guarded glyph
    atlas smoke script. Full runs the Release test suite and can optionally add
    GlyphSmoke.

.EXAMPLE
    .\scripts\validate.ps1

.EXAMPLE
    .\scripts\validate.ps1 -Mode Focused

.EXAMPLE
    .\scripts\validate.ps1 -Mode Full -IncludeGlyphSmoke
#>

[CmdletBinding()]
param(
    [ValidateSet("Quick", "Focused", "GlyphSmoke", "Full")]
    [string]$Mode = "Quick",

    [switch]$IncludeGlyphSmoke,

    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$solution = Join-Path $repoRoot "Irix.slnx"
$glyphRegression = Join-Path $scriptDir "glyph-atlas-regression.ps1"
$results = New-Object System.Collections.Generic.List[object]

if (-not (Test-Path $solution)) {
    Write-Error "Irix solution not found at: $solution"
    exit 1
}

function Invoke-ValidationLane {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    Write-Host "=== $Name ===" -ForegroundColor Cyan
    $started = Get-Date
    $exitCode = 0
    try {
        & $Command
        $exitCode = if ($LASTEXITCODE -is [int]) { $LASTEXITCODE } else { 0 }
    }
    catch {
        Write-Host $_ -ForegroundColor Red
        $exitCode = 1
    }

    $elapsed = (Get-Date) - $started
    $status = if ($exitCode -eq 0) { "Passed" } else { "Failed" }
    $results.Add([pscustomobject]@{
        Name = $Name
        Status = $status
        ExitCode = $exitCode
        DurationSeconds = [math]::Round($elapsed.TotalSeconds, 1)
    }) | Out-Null

    if ($exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode."
    }
}

function Invoke-Dotnet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-Quick {
    if (-not $NoRestore) {
        Invoke-ValidationLane "Restore runtime" {
            Invoke-Dotnet @("restore", $solution)
        }
        Invoke-ValidationLane "Restore diagnostics" {
            Invoke-Dotnet @("restore", $solution, "-p:IrixDiagnostics=true")
        }
    }

    Invoke-ValidationLane "Release build" {
        $args = @("build", $solution, "--configuration", "Release")
        if ($NoRestore) {
            $args += "--no-restore"
        }
        Invoke-Dotnet $args
    }

    Invoke-ValidationLane "Quick tests" {
        Invoke-Dotnet @(
            "test",
            $solution,
            "--configuration",
            "Release",
            "--no-build",
            "--filter",
            "Category!=D3D12&Category!=Performance",
            "--verbosity",
            "normal")
    }
}

function Invoke-Focused {
    if (-not $NoRestore) {
        Invoke-ValidationLane "Restore diagnostics" {
            Invoke-Dotnet @("restore", $solution, "-p:IrixDiagnostics=true")
        }
    }

    Invoke-ValidationLane "Focused source and diagnostics guards" {
        Invoke-Dotnet @(
            "test",
            $solution,
            "--configuration",
            "Release",
            "--no-restore",
            "--filter",
            "FullyQualifiedName~ProgramDiagnosticsTests",
            "--verbosity",
            "normal")
    }

    Invoke-ValidationLane "Focused partial apply and handoff guards" {
        Invoke-Dotnet @(
            "test",
            $solution,
            "--configuration",
            "Release",
            "--no-restore",
            "--filter",
            "FullyQualifiedName~PartialApply|FullyQualifiedName~DrawingBackendCompositor",
            "--verbosity",
            "normal")
    }

    Invoke-ValidationLane "Focused composition and scroll boundary guards" {
        Invoke-Dotnet @(
            "test",
            $solution,
            "--configuration",
            "Release",
            "--no-restore",
            "--filter",
            "FullyQualifiedName~Composition|FullyQualifiedName~Scroll|FullyQualifiedName~CounterInputRouter|FullyQualifiedName~WindowLayoutPipeline",
            "--verbosity",
            "normal")
    }
}

function Invoke-GlyphSmoke {
    if (-not (Test-Path $glyphRegression)) {
        throw "Glyph atlas regression script not found at: $glyphRegression"
    }

    Invoke-ValidationLane "Glyph atlas smoke" {
        & $glyphRegression -Mode Smoke
        if ($LASTEXITCODE -ne 0) {
            throw "glyph-atlas-regression.ps1 failed with exit code $LASTEXITCODE."
        }
    }
}

function Invoke-Full {
    if (-not $NoRestore) {
        Invoke-ValidationLane "Restore diagnostics" {
            Invoke-Dotnet @("restore", $solution, "-p:IrixDiagnostics=true")
        }
    }

    Invoke-ValidationLane "Full Release tests" {
        Invoke-Dotnet @(
            "test",
            $solution,
            "--configuration",
            "Release",
            "--no-restore",
            "--verbosity",
            "normal")
    }

    if ($IncludeGlyphSmoke) {
        Invoke-GlyphSmoke
    }
}

try {
    Write-Host "Irix local validation: mode=$Mode includeGlyphSmoke=$IncludeGlyphSmoke noRestore=$NoRestore" -ForegroundColor Cyan
    Write-Host "Repository: $repoRoot"
    Write-Host ""

    switch ($Mode) {
        "Quick" { Invoke-Quick }
        "Focused" { Invoke-Focused }
        "GlyphSmoke" { Invoke-GlyphSmoke }
        "Full" { Invoke-Full }
        default { throw "Unknown validation mode '$Mode'." }
    }

    Write-Host ""
    Write-Host "=== Validation summary ===" -ForegroundColor Cyan
    foreach ($result in $results) {
        Write-Host ("{0}: status={1} exitCode={2} durationSeconds={3}" -f $result.Name, $result.Status, $result.ExitCode, $result.DurationSeconds)
    }
    Write-Host "validation.guard status=Passed mode=$Mode lanes=$($results.Count)" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ""
    Write-Host "=== Validation summary ===" -ForegroundColor Cyan
    foreach ($result in $results) {
        $color = if ($result.Status -eq "Passed") { "Gray" } else { "Red" }
        Write-Host ("{0}: status={1} exitCode={2} durationSeconds={3}" -f $result.Name, $result.Status, $result.ExitCode, $result.DurationSeconds) -ForegroundColor $color
    }
    Write-Host "validation.guard status=Failed mode=$Mode lanes=$($results.Count)" -ForegroundColor Red
    Write-Error $_
    exit 1
}
