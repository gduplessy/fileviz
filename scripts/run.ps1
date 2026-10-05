$ErrorActionPreference='Stop'
Set-Location -LiteralPath (Join-Path $PSScriptRoot '..')
$fileVizSdk=Join-Path $PWD '.tools\dotnet\dotnet.exe'
if(!(Test-Path -LiteralPath $fileVizSdk)){$fileVizSdk=(Get-Command dotnet).Source}
$env:DOTNET_ROOT=Split-Path $fileVizSdk
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
& $fileVizSdk run --project src/FileViz.App -c Release
exit $LASTEXITCODE