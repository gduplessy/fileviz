param([string]$ReleaseDirectory='artifacts/release')
$ErrorActionPreference='Stop'
$fileVizRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fileVizRelease=[IO.Path]::GetFullPath((Join-Path $fileVizRoot $ReleaseDirectory))
if(!$fileVizRelease.StartsWith($fileVizRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Package validation must use workspace-owned packages.'}
$fileVizOutput=Join-Path $fileVizRoot ('artifacts/package-validation-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $fileVizOutput|Out-Null
$fileVizFixture=Join-Path $fileVizOutput 'fixture';New-Item -ItemType Directory -Path $fileVizFixture|Out-Null
$fileVizRandom=[Random]::new(42);$fileVizBuffer=[byte[]]::new(131072)
0..299|ForEach-Object{$fileVizRandom.NextBytes($fileVizBuffer);[IO.File]::WriteAllBytes((Join-Path $fileVizFixture ('sample-'+$_.ToString('D3')+'.bin')),$fileVizBuffer)}
$fileVizDuplicate=[byte[]]::new(1048576);$fileVizRandom.NextBytes($fileVizDuplicate)
[IO.File]::WriteAllBytes((Join-Path $fileVizFixture 'duplicate-a.bin'),$fileVizDuplicate)
[IO.File]::WriteAllBytes((Join-Path $fileVizFixture 'duplicate-b.bin'),$fileVizDuplicate)
[IO.File]::WriteAllText((Join-Path $fileVizFixture 'Unicode-文件.txt'),'Disposable FileViz fixture')
Get-Content -LiteralPath (Join-Path $fileVizRelease 'SHA256SUMS.txt')|ForEach-Object{
    if($_ -notmatch '^([0-9a-f]{64})  ([^/\\]+)$'){throw 'Invalid checksum entry.'}
    $fileVizExpected=$Matches[1];$fileVizAsset=Join-Path $fileVizRelease $Matches[2]
    if((Get-FileHash -LiteralPath $fileVizAsset -Algorithm SHA256).Hash.ToLowerInvariant() -ne $fileVizExpected){throw 'Package checksum mismatch.'}
}
$fileVizZip=Get-ChildItem -LiteralPath $fileVizRelease -Filter '*-portable.zip'|Select-Object -First 1
$fileVizInstaller=Get-ChildItem -LiteralPath $fileVizRelease -Filter '*-setup.exe'|Select-Object -First 1
$fileVizPortable=Join-Path $fileVizOutput 'portable'
Expand-Archive -LiteralPath $fileVizZip.FullName -DestinationPath $fileVizPortable
$fileVizOldPath=$env:PATH;$fileVizOldDotnet=$env:DOTNET_ROOT;$fileVizOldDotnetX64=$env:DOTNET_ROOT_X64
$env:PATH=($env:PATH.Split(';')|Where-Object{$_ -notmatch '(?i)dotnet'}) -join ';'
Remove-Item Env:DOTNET_ROOT,Env:DOTNET_ROOT_X64 -ErrorAction SilentlyContinue
function Invoke-FileVizSmoke($exe,$label){
    $destination=Join-Path $fileVizOutput $label;New-Item -ItemType Directory -Path $destination|Out-Null
    $process=Start-Process -FilePath $exe -ArgumentList '--smoke',('"'+$fileVizFixture+'"'),('"'+$destination+'"') -WindowStyle Normal -PassThru
    if(!$process.WaitForExit(60000)){if(!$process.CloseMainWindow()){$process.Kill()};throw 'Desktop package smoke timed out.'}
    if($process.ExitCode -ne 0){throw ('Desktop package smoke failed: '+$process.ExitCode)}
    Get-Content -LiteralPath (Join-Path $destination 'smoke.json') -Raw|ConvertFrom-Json
}
$fileVizUninstallKey='Software\Microsoft\Windows\CurrentVersion\Uninstall\{ADC30985-9B15-49CB-AEC7-E9B8D3A02281}_is1'
$fileVizInstall=Join-Path $fileVizOutput 'installed';$fileVizInstalled=$false
try{
    $portableResult=Invoke-FileVizSmoke (Join-Path $fileVizPortable 'FileViz.exe') 'portable-smoke'
    if(Test-Path -LiteralPath ('HKCU:\'+$fileVizUninstallKey)){throw 'An existing user FileViz installation must not be overwritten by package validation.'}
    $setup=Start-Process -FilePath $fileVizInstaller.FullName -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR="'+$fileVizInstall+'"'),('/LOG="'+(Join-Path $fileVizOutput 'install.log')+'"') -WindowStyle Hidden -Wait -PassThru
    if($setup.ExitCode -ne 0){throw ('Per-user install failed: '+$setup.ExitCode)}
    $fileVizInstalled=$true
    $installedResult=Invoke-FileVizSmoke (Join-Path $fileVizInstall 'FileViz.exe') 'installed-smoke'
    $uninstall=Start-Process -FilePath (Join-Path $fileVizInstall 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -WindowStyle Hidden -Wait -PassThru
    if($uninstall.ExitCode -ne 0 -or (Test-Path -LiteralPath (Join-Path $fileVizInstall 'FileViz.exe'))){throw 'Owned test installation did not uninstall successfully.'}
    $fileVizInstalled=$false
    [ordered]@{Portable=$portableResult;Installed=$installedResult;InstallerExitCode=$setup.ExitCode;UninstallerExitCode=$uninstall.ExitCode;ChecksumsVerified=$true;DeveloperRuntimeRemovedFromEnvironment=$true;OS=[Environment]::OSVersion.VersionString;ValidatedUtc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $fileVizOutput 'result.json') -Encoding utf8
    Write-Output $fileVizOutput
}finally{
    $env:PATH=$fileVizOldPath;$env:DOTNET_ROOT=$fileVizOldDotnet;$env:DOTNET_ROOT_X64=$fileVizOldDotnetX64
    if($fileVizInstalled){Write-Warning ('Retained owned failed test installation at '+$fileVizInstall+'; inspect the logs before removal.')}
}