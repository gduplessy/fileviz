$ErrorActionPreference='Stop'
Set-Location -LiteralPath (Join-Path $PSScriptRoot '..')
$fileVizVersion=(Get-Content -LiteralPath global.json -Raw|ConvertFrom-Json).sdk.version
$fileVizTools=Join-Path $PWD '.tools';$fileVizSdk=Join-Path $fileVizTools 'dotnet'
if(!(Test-Path -LiteralPath (Join-Path $fileVizSdk ('sdk\'+$fileVizVersion)))){
    New-Item -ItemType Directory -Force -Path $fileVizTools|Out-Null
    $fileVizInstaller=Join-Path $fileVizTools 'dotnet-install.ps1'
    Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $fileVizInstaller
    & $fileVizInstaller -Version $fileVizVersion -InstallDir $fileVizSdk -NoPath
    if($LASTEXITCODE -ne 0){throw 'Pinned SDK installation failed.'}
}
$env:DOTNET_ROOT=$fileVizSdk;$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
& (Join-Path $fileVizSdk 'dotnet.exe') --version
if($LASTEXITCODE -ne 0){throw 'SDK verification failed.'}