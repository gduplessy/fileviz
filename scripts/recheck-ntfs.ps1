param([string]$ImagePath,[string]$Benchmark='artifacts\ntfs-build\FileViz.Benchmarks.dll')
$ErrorActionPreference='Stop'
$fileVizRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fileVizImagePath=[IO.Path]::GetFullPath((Join-Path $fileVizRoot $ImagePath))
if(!$fileVizImagePath.StartsWith((Join-Path $fileVizRoot 'artifacts\ntfs-validation-'),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($fileVizImagePath) -ne 'fixture.vhdx'){throw 'Expected a dedicated FileViz test image inside artifacts.'}
$fileVizOutput=Join-Path $fileVizRoot ('artifacts\ntfs-recheck-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'));New-Item -ItemType Directory -Path $fileVizOutput|Out-Null
Start-Transcript (Join-Path $fileVizOutput 'validation.log')|Out-Null
$fileVizAttached=$false
try {
    $fileVizImage=Mount-DiskImage -ImagePath $fileVizImagePath -PassThru
    $fileVizAttached=$true
    $fileVizVolume=$fileVizImage|Get-Disk|Get-Partition|Get-Volume|Where-Object {$_.FileSystemLabel -eq 'FileVizTest'}|Select-Object -First 1
    if(!$fileVizVolume -or !$fileVizVolume.DriveLetter){throw 'Expected a mounted FileVizTest NTFS volume.'}
    $fileVizDrive=$fileVizVolume.DriveLetter+':\'
    $env:DOTNET_ROOT=Join-Path $fileVizRoot '.tools\dotnet'
    $fileVizDotnet=Join-Path $env:DOTNET_ROOT 'dotnet.exe';$fileVizBenchmark=Join-Path $fileVizRoot $Benchmark
    & $fileVizDotnet $fileVizBenchmark --parity $fileVizDrive (Join-Path $fileVizOutput 'parity')
    if($LASTEXITCODE -ne 0){throw 'Parity failed.'}
    & $fileVizDotnet $fileVizBenchmark --scan $fileVizDrive (Join-Path $fileVizOutput 'directory') directory
    if($LASTEXITCODE -ne 0){throw 'Directory benchmark failed.'}
    & $fileVizDotnet $fileVizBenchmark --scan $fileVizDrive (Join-Path $fileVizOutput 'mft') mft
    if($LASTEXITCODE -ne 0){throw 'MFT benchmark failed.'}
} catch {$_|Out-String|Set-Content (Join-Path $fileVizOutput 'error.txt');throw}
finally{if($fileVizAttached){Dismount-DiskImage -ImagePath $fileVizImagePath};Stop-Transcript|Out-Null}