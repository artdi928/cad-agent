# Start CadAgent MCP stdio server in Release mode
$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$mcpExe = Join-Path $scriptDir "src\CadAgent.Mcp\bin\Release\net8.0\CadAgent.Mcp.exe"
if (Test-Path $mcpExe) {
    & $mcpExe
} else {
    dotnet run --project (Join-Path $scriptDir "src\CadAgent.Mcp\CadAgent.Mcp.csproj") -c Release
}
