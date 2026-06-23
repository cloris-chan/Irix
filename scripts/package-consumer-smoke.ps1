[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+-.+')]
    [string]$PackageVersion = '0.1.0-preview.1',

    [Parameter()]
    [string]$PackageSource = 'artifacts\nuget-preview',

    [Parameter()]
    [string]$WorkingPath = 'artifacts\package-consumer-smoke'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$sourceRoot = (Resolve-Path (Join-Path $repoRoot $PackageSource)).ProviderPath
$workingRoot = Join-Path $repoRoot $WorkingPath
$projectRoot = Join-Path $workingRoot 'Irix.Poc.PackageSmoke'
$sourcePocRoot = Join-Path $repoRoot 'src\Irix.Poc'
$packagesRoot = Join-Path $projectRoot '.packages'

if (Test-Path $workingRoot) {
    Remove-Item -LiteralPath $workingRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $projectRoot | Out-Null

Get-ChildItem -LiteralPath $sourcePocRoot -Filter '*.cs' |
    Where-Object { $_.Name -notlike '*.optional-diagnostics.cs' } |
    Copy-Item -Destination $projectRoot

$manifest = Join-Path $sourcePocRoot 'app.manifest'
if (Test-Path $manifest) {
    Copy-Item -LiteralPath $manifest -Destination $projectRoot
}

$projectPath = Join-Path $projectRoot 'Irix.Poc.csproj'
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>`$(IrixWindowsTargetFramework)</TargetFramework>
    <SupportedOSPlatformVersion>`$(IrixWindowsSupportedOSPlatformVersion)</SupportedOSPlatformVersion>
    <OutputType>Exe</OutputType>
    <AssemblyName>Irix.Poc</AssemblyName>
    <RootNamespace>Irix.Poc</RootNamespace>
    <PlatformTarget>x64</PlatformTarget>
    <PublishAot>false</PublishAot>
    <IsPackable>false</IsPackable>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <RestorePackagesPath>$packagesRoot</RestorePackagesPath>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Irix" Version="$PackageVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $projectPath -Encoding UTF8

$nugetConfigPath = Join-Path $projectRoot 'NuGet.config'
$escapedSourceRoot = [System.Security.SecurityElement]::Escape($sourceRoot)
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-preview" value="$escapedSourceRoot" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath $nugetConfigPath -Encoding UTF8

dotnet restore $projectPath --configfile $nugetConfigPath --no-cache --force-evaluate
if ($LASTEXITCODE -ne 0) {
    throw "Package consumer restore failed with exit code $LASTEXITCODE."
}

dotnet build $projectPath -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "Package consumer build failed with exit code $LASTEXITCODE."
}

$output = dotnet run --project $projectPath -c Release --no-build -- --package-smoke
if ($LASTEXITCODE -ne 0) {
    throw "Package consumer smoke failed with exit code $LASTEXITCODE."
}

if ($output -notmatch 'package-smoke count=1 commands=\d+ hitTargets=\d+ resources=\S+') {
    throw "Package consumer smoke produced unexpected output: $output"
}

Write-Host $output
