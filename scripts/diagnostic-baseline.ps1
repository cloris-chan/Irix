<#
.SYNOPSIS
    Runs local diagnostic baselines with repeatable file names.

.EXAMPLE
    .\scripts\diagnostic-baseline.ps1 -Mode Sync -RefreshLabel 60Hz -ScalePercent 150

.EXAMPLE
    .\scripts\diagnostic-baseline.ps1 -Mode All -RefreshLabel 240Hz -ScalePercent 150 -Aot

.EXAMPLE
    .\scripts\diagnostic-baseline.ps1 -Mode Smoke -RefreshLabel 60Hz -ScalePercent 150 -PartialMode NoPartialApply
#>

param(
    [ValidateSet("Sync", "TextCache", "Smoke", "All")]
    [string]$Mode = "All",

    [int]$Frames = 300,

    [int]$Samples = 3,

    [int]$TextCacheFrames = 180,

    [string]$RefreshLabel = "current",

    [int]$ScalePercent = 0,

    [switch]$Aot,

    [switch]$SeparateSamples,

    [ValidateSet("Default", "NoPartialApply")]
    [string]$PartialMode = "Default"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$pocProject = Join-Path $repoRoot "src\Irix.Poc\Irix.Poc.csproj"
$resultsDir = Join-Path $repoRoot "TestResults"
$script:CachedRunner = $null

if (-not (Test-Path $pocProject)) {
    throw "Irix.Poc project not found at: $pocProject"
}

if (-not (Test-Path $resultsDir)) {
    New-Item -ItemType Directory -Path $resultsDir | Out-Null
}

function ConvertTo-Label([string]$Value) {
    $label = $Value.Trim().ToLowerInvariant()
    $label = $label -replace '[^a-z0-9]+', '-'
    $label = $label.Trim('-')
    if ([string]::IsNullOrWhiteSpace($label)) { return "current" }
    return $label
}

function Get-RunnerCommand {
    if ($null -ne $script:CachedRunner) {
        return $script:CachedRunner
    }

    if (-not $Aot) {
        $script:CachedRunner = [pscustomobject]@{
            Command = "dotnet"
            Args = @("run", "--project", $pocProject, "-c", "Release", "-p:IrixDiagnostics=true", "--")
            UseStartProcess = $false
        }
        return $script:CachedRunner
    }

    dotnet publish $pocProject -c Release -r win-x64 --self-contained -p:IrixDiagnostics=true | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "AOT publish failed with exit code $LASTEXITCODE."
    }

    $publishRoot = Join-Path (Split-Path -Parent $pocProject) "bin\diagnostics\Release"
    $exe = Get-ChildItem $publishRoot -Recurse -Filter "Irix.Poc.exe" | Where-Object { $_.FullName -match '\\publish\\Irix\.Poc\.exe$' } | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -eq $exe) {
        throw "Published Irix.Poc.exe was not found under $publishRoot."
    }

    $script:CachedRunner = [pscustomobject]@{
        Command = $exe.FullName
        Args = @()
        UseStartProcess = $true
    }
    return $script:CachedRunner
}

function Invoke-Runner([string[]]$Arguments) {
    $runner = Get-RunnerCommand
    $command = [string]$runner.Command
    $baseArgs = [string[]]$runner.Args
    $allArgs = @($baseArgs) + @($Arguments)

    if ($runner.UseStartProcess) {
        $process = Start-Process -FilePath $command -ArgumentList $allArgs -WorkingDirectory $repoRoot -PassThru -Wait
        return $process.ExitCode
    }

    & $command @allArgs | Out-Host
    return $LASTEXITCODE
}

function Invoke-CapturedDiagnostic([string]$Name, [string[]]$Arguments, [string[]]$SummaryPatterns) {
    $outputPath = Join-Path $resultsDir $Name
    $summaryPath = [System.IO.Path]::ChangeExtension($outputPath, ".summary.txt")
    $diagnosticArgs = @($Arguments) + @("--diagnostic-output", $outputPath)

    Write-Host "=== Running $Name ===" -ForegroundColor Cyan
    Remove-Item $outputPath, $summaryPath -Force -ErrorAction SilentlyContinue
    $exitCode = Invoke-Runner $diagnosticArgs
    if ($exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode. Output: $outputPath"
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
    return $summaryLines
}

function Invoke-SeparateSyncSamples {
    $combinedName = "diagnose-sync-$refresh-$scale-$runtime-separate.summary.txt"
    $combinedPath = Join-Path $resultsDir $combinedName
    $combinedLines = @(
        "RefreshLabel: $RefreshLabel",
        "ScaleLabel: $scale",
        "Runtime: $runtime",
        "FramesPerSample: $Frames",
        "ProcessSamples: $Samples"
    )

    for ($i = 1; $i -le $Samples; $i++) {
        $sampleLabel = "{0:d2}" -f $i
        $lines = Invoke-CapturedDiagnostic `
            "diagnose-sync-$refresh-$scale-$runtime-sample$sampleLabel.txt" `
            @("--diagnose-sync", "$Frames", "1") `
            @("Display refresh", "Display scale", "Text composition mode", "--- Sample", "Sync wait:", "Waits >2ms", "Frame time:", "Verdict:", "Final:")

        $combinedLines += "--- Process sample $i/$Samples ---"
        $combinedLines += $lines
    }

    $combinedLines | Set-Content -Encoding UTF8 $combinedPath
    Write-Host "Combined summary: $combinedPath" -ForegroundColor Cyan
}

function Invoke-SmokeRun {
    $args = @()

    if ($PartialMode -eq "NoPartialApply") {
        $args += "--no-partial-apply"
    }

    Write-Host "=== Starting manual smoke run ===" -ForegroundColor Cyan
    Write-Host "Runtime: $(if ($Aot) { 'AOT' } else { 'non-AOT' })"
    Write-Host "Partial mode: $PartialMode"
    Write-Host "Close the Irix window when the manual observation is complete."
    $exitCode = Invoke-Runner $args
    if ($exitCode -ne 0) {
        throw "Smoke run failed with exit code $exitCode."
    }
}

$refresh = ConvertTo-Label $RefreshLabel
$scale = if ($ScalePercent -gt 0) { "$ScalePercent`pct" } else { "current-scale" }
$runtime = if ($Aot) { "aot" } else { "non-aot" }

Write-Host "Diagnostic baseline context: refresh=$RefreshLabel, scale=$scale, runtime=$runtime, partial=$PartialMode"
Write-Host ""

if ($Mode -eq "Sync" -or $Mode -eq "All") {
    if ($SeparateSamples -and $Samples -gt 1) {
        Invoke-SeparateSyncSamples
    } else {
        Invoke-CapturedDiagnostic `
            "diagnose-sync-$refresh-$scale-$runtime.txt" `
            @("--diagnose-sync", "$Frames", "$Samples") `
            @("Display refresh", "Display scale", "Text composition mode", "--- Sample", "Sync wait:", "Waits >2ms", "Frame time:", "Verdict:", "Avg sync wait range", "P95 sync wait range", "Max sync wait range", "Final:") | Out-Null
    }
}

if ($Mode -eq "TextCache" -or $Mode -eq "All") {
    Invoke-CapturedDiagnostic `
        "diagnose-text-cache-$refresh-$scale-$runtime.txt" `
        @("--diagnose-text-cache", "$TextCacheFrames") `
        @("Display refresh", "Display scale", "--- Scenario", "Format cache", "Layout cache", "Allocation:", "FrameDrawingResources", "complete") | Out-Null
}

if ($Mode -eq "Smoke") {
    Invoke-SmokeRun
}
