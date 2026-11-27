# Requires the .NET 10 SDK.
param(
    [Alias("Arquitetura")]
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Architecture = "win-x64",
    [switch]$SelfContained,
    # Full version to build, for example 0.13.1-nightly.20261122.57; Directory.Build.props is used when empty.
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$output = Join-Path $PSScriptRoot "dist/$Architecture"
# Publish into a fresh staging folder so files from earlier builds never leak into the package.
$staging = Join-Path $PSScriptRoot "dist/.staging-$Architecture"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
$includeRuntime = $SelfContained.IsPresent.ToString().ToLowerInvariant()
$projects = @(
    "src/AppTunnel.Proton/AppTunnel.Proton.csproj",
    "src/AppTunnel.Windows/AppTunnel.Windows.csproj"
)
foreach ($project in $projects) {
    # Built with += so it stays an array: a one-item array returned from "if" unwraps into a string, and splatting a
    # string passes it one character at a time.
    $versionArgs = @()
    if ($Version) { $versionArgs += "-p:Version=$Version" }
    dotnet publish (Join-Path $PSScriptRoot $project) -c Release -r $Architecture --self-contained $includeRuntime -p:DebugType=None @versionArgs -o $staging
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# Assemble the complete package here so the ZIP, local installer builds and CI all ship the same files.
Copy-Item (Join-Path $PSScriptRoot "README.md"), (Join-Path $PSScriptRoot "README.pt-BR.md"), (Join-Path $PSScriptRoot "LEIA-ME.md"), (Join-Path $PSScriptRoot "LICENSE") $staging
Copy-Item (Join-Path $PSScriptRoot "docs") $staging -Recurse
Copy-Item (Join-Path $PSScriptRoot "Licenses") $staging -Recurse
Copy-Item (Join-Path $PSScriptRoot "src/AppTunnel.Windows/Fonts/*-OFL.txt") (Join-Path $staging "Licenses")

if (Test-Path $output) { Remove-Item $output -Recurse -Force }
Move-Item $staging $output
Write-Host "Release files are in $output"
