# Requires the .NET 10 SDK.
param(
    [Alias("Arquitetura")]
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Architecture = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$output = Join-Path $PSScriptRoot "dist/$Architecture"
$includeRuntime = $SelfContained.IsPresent.ToString().ToLowerInvariant()
$projects = @(
    "src/AppTunnel.Proton/AppTunnel.Proton.csproj",
    "src/AppTunnel.Windows/AppTunnel.Windows.csproj"
)
foreach ($project in $projects) {
    dotnet publish (Join-Path $PSScriptRoot $project) -c Release -r $Architecture --self-contained $includeRuntime -p:DebugType=None -o $output
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Write-Host "Release files are in $output"
