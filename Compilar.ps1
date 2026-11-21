# Requires the .NET 10 SDK.
param(
    [Alias("Arquitetura")]
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Architecture = "win-x64",
    [switch]$SelfContained
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
    dotnet publish (Join-Path $PSScriptRoot $project) -c Release -r $Architecture --self-contained $includeRuntime -p:DebugType=None -o $staging
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# Assemble the complete package here so the ZIP, local installer builds and CI all ship the same files.
Copy-Item (Join-Path $PSScriptRoot "README.md"), (Join-Path $PSScriptRoot "LEIA-ME.md"), (Join-Path $PSScriptRoot "LICENSE") $staging
Copy-Item (Join-Path $PSScriptRoot "docs") $staging -Recurse
Copy-Item (Join-Path $PSScriptRoot "Licenses") $staging -Recurse
Copy-Item (Join-Path $PSScriptRoot "src/AppTunnel.Windows/Fonts/*-OFL.txt") (Join-Path $staging "Licenses")

if (Test-Path $output) { Remove-Item $output -Recurse -Force }
Move-Item $staging $output
Write-Host "Release files are in $output"
