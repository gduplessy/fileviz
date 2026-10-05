param([Parameter(ValueFromRemainingArguments=$true)][string[]]$DotnetArguments)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath (Join-Path $PSScriptRoot '..')
$fileVizSdk=Join-Path $PWD '.tools\dotnet\dotnet.exe'
if(!(Test-Path -LiteralPath $fileVizSdk)){$fileVizSdk=(Get-Command dotnet -ErrorAction Stop).Source}
$env:DOTNET_ROOT=Split-Path $fileVizSdk
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
& $fileVizSdk @DotnetArguments
exit $LASTEXITCODE