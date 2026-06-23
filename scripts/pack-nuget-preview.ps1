[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+-.+')]
    [string]$PackageVersion = '0.1.0-preview.1',

    [Parameter()]
    [string]$OutputPath = 'artifacts\nuget-preview',

    [Parameter()]
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$outputRoot = Join-Path $repoRoot $OutputPath
$expectedPackages = @(
    'Irix',
    'Irix.Core',
    'Irix.Drawing',
    'Irix.Platform',
    'Irix.Rendering',
    'Irix.Platform.Windows'
)

if (Test-Path $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $outputRoot | Out-Null

if (-not $NoRestore) {
    dotnet restore (Join-Path $repoRoot 'Irix.slnx')
}

$packArgs = @(
    'pack',
    (Join-Path $repoRoot 'Irix.slnx'),
    '-c',
    'Release',
    '-o',
    $outputRoot,
    "/p:PackageVersion=$PackageVersion"
)

if ($NoRestore) {
    $packArgs += '--no-restore'
}

dotnet @packArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed with exit code $LASTEXITCODE."
}

$packages = Get-ChildItem -LiteralPath $outputRoot -Filter '*.nupkg' | Sort-Object Name
$actualPackageIds = @()
foreach ($package in $packages) {
    $match = [regex]::Match($package.Name, "^(?<id>.+)\.$([regex]::Escape($PackageVersion))\.nupkg$")
    if (-not $match.Success) {
        throw "Unexpected package file '$($package.Name)' for version '$PackageVersion'."
    }

    $actualPackageIds += $match.Groups['id'].Value
}

$missing = $expectedPackages | Where-Object { $_ -notin $actualPackageIds }
if ($missing.Count -gt 0) {
    throw "Missing NuGet packages: $($missing -join ', ')."
}

$unexpected = $actualPackageIds | Where-Object { $_ -notin $expectedPackages }
if ($unexpected.Count -gt 0) {
    throw "Unexpected NuGet packages: $($unexpected -join ', ')."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($package in $packages) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        if (-not ($zip.Entries | Where-Object { $_.FullName -eq 'package-readme.md' })) {
            throw "Package '$($package.Name)' does not contain package-readme.md at the package root."
        }

        $nuspec = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
        if ($null -eq $nuspec) {
            throw "Package '$($package.Name)' does not contain a nuspec."
        }

        $reader = New-Object System.IO.StreamReader($nuspec.Open())
        try {
            [xml]$document = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $metadata = $document.package.metadata
        if ([string]::IsNullOrWhiteSpace($metadata.description) -or $metadata.description -eq 'Package Description') {
            throw "Package '$($package.Name)' has an invalid description."
        }

        if ($metadata.readme -ne 'package-readme.md') {
            throw "Package '$($package.Name)' has readme '$($metadata.readme)' instead of package-readme.md."
        }
    }
    finally {
        $zip.Dispose()
    }
}

Write-Host "Packed NuGet preview packages:"
foreach ($package in $packages) {
    Write-Host "  $($package.Name)"
}
