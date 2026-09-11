[CmdletBinding()]
param(
    [string]$BuildConfiguration = "Release",
    [string]$Destination = (Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\CadAgent.bundle")
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo "src\CadAgent.Plugin\bin\$BuildConfiguration\net8.0-windows"
if (-not (Test-Path -LiteralPath (Join-Path $output "CadAgent.Plugin.dll"))) {
    throw "Build the plugin first: dotnet build CadAgent.sln -c $BuildConfiguration"
}
$contents = Join-Path $Destination "Contents"
New-Item -ItemType Directory -Force -Path $contents | Out-Null
Copy-Item -LiteralPath (Join-Path $repo "installer\PackageContents.xml") -Destination $Destination -Force
Get-ChildItem -LiteralPath $output -File | Where-Object Extension -In ".dll", ".json", ".pdb" |
    Copy-Item -Destination $contents -Force
Write-Host "Installed $Destination. Restart AutoCAD, then run ECA_PING and ECA_STATUS."
