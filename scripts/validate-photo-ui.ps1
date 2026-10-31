param([string]$AppDirectory='src/FileViz.App/bin/Release/net10.0-windows', [int]$TimeoutSeconds=120)
$ErrorActionPreference='Stop'
if($TimeoutSeconds -lt 1 -or $TimeoutSeconds -gt 600){throw 'Use a timeout from 1 to 600 seconds.'}
$fileVizRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fileVizApp=[IO.Path]::GetFullPath((Join-Path $fileVizRoot $AppDirectory))
if(!$fileVizApp.StartsWith($fileVizRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Use a workspace-owned build.'}
if(!(Test-Path (Join-Path $fileVizApp 'FileViz.exe'))){throw 'Build the desktop app before running its photo UI checks.'}
$fileVizOutput=Join-Path $fileVizRoot ('artifacts/photo-ui-validation-'+[Guid]::NewGuid().ToString('N'))
$fileVizFixture=Join-Path $fileVizOutput 'fixture';$fileVizSmoke=Join-Path $fileVizOutput 'smoke'
New-Item -ItemType Directory -Path $fileVizFixture,$fileVizSmoke | Out-Null
[IO.File]::WriteAllText((Join-Path $fileVizFixture 'duplicate-a.bin'),'Disposable identical payload')
[IO.File]::WriteAllText((Join-Path $fileVizFixture 'duplicate-b.bin'),'Disposable identical payload')
[IO.File]::WriteAllText((Join-Path $fileVizFixture 'different.txt'),'Different disposable payload')
$fileVizOldPhotos=$env:FILEVIZ_SMOKE_PHOTOS;$fileVizOldRoot=$env:DOTNET_ROOT;$fileVizOldRootX64=$env:DOTNET_ROOT_X64
try{
    $env:FILEVIZ_SMOKE_PHOTOS='1'
    $fileVizLocalRuntime=Join-Path $fileVizRoot '.tools/dotnet'
    if(Test-Path (Join-Path $fileVizLocalRuntime 'dotnet.exe')){$env:DOTNET_ROOT=$fileVizLocalRuntime;$env:DOTNET_ROOT_X64=$fileVizLocalRuntime}
    $fileVizProcess=Start-Process -FilePath (Join-Path $fileVizApp 'FileViz.exe') -ArgumentList '--smoke',('"'+$fileVizFixture+'"'),('"'+$fileVizSmoke+'"') -WindowStyle Normal -PassThru
    $fileVizTimer=[Diagnostics.Stopwatch]::StartNew()
    while(!$fileVizProcess.WaitForExit(1000)){if($fileVizTimer.Elapsed.TotalSeconds -gt $TimeoutSeconds){throw ('Photo UI checks timed out; retained fixture: '+$fileVizOutput)}}
    if($fileVizProcess.ExitCode -ne 0){Get-Content (Join-Path $fileVizSmoke 'smoke-error.txt') -ErrorAction SilentlyContinue;throw 'Photo UI checks failed.'}
    $fileVizResult=Get-Content (Join-Path $fileVizSmoke 'smoke.json') -Raw|ConvertFrom-Json
    if(!$fileVizResult.PhotosAutoPreviewValidated -or !$fileVizResult.PhotosBulkSelectionValidated){throw 'Photo UI assertions did not complete.'}
    [ordered]@{SmokeExitCode=$fileVizProcess.ExitCode;Desktop=$fileVizResult;ValidatedUtc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $fileVizOutput 'result.json') -Encoding utf8
    Write-Output $fileVizOutput
}finally{
    $env:FILEVIZ_SMOKE_PHOTOS=$fileVizOldPhotos;$env:DOTNET_ROOT=$fileVizOldRoot;$env:DOTNET_ROOT_X64=$fileVizOldRootX64
}
