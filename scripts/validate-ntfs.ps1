param([int]$FileCount=10000,[string]$ReservedLetters='')
$ErrorActionPreference='Stop'
$fileVizWorkspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fileVizOutput=Join-Path $fileVizWorkspace ('artifacts\ntfs-validation-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $fileVizOutput | Out-Null
$fileVizVhd=Join-Path $fileVizOutput 'fixture.vhdx'
if(!$fileVizVhd.StartsWith($fileVizWorkspace+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'VHD target is outside the workspace.'}
if(Test-Path -LiteralPath $fileVizVhd){throw 'Refusing to reuse an existing disk image.'}
if(!([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))){throw 'Run this disposable-volume validation as administrator.'}
$fileVizAttached=$false
Start-Transcript -Path (Join-Path $fileVizOutput 'validation.log') | Out-Null
try {
    $fileVizDiskpart=Join-Path $fileVizOutput 'create.txt'
    [IO.File]::WriteAllText($fileVizDiskpart,('create vdisk file="'+$fileVizVhd+'" maximum=1024 type=expandable'+[Environment]::NewLine+'select vdisk file="'+$fileVizVhd+'"'+[Environment]::NewLine+'attach vdisk'+[Environment]::NewLine+'exit'))
    & diskpart.exe /s $fileVizDiskpart
    $fileVizImage=Get-DiskImage -ImagePath $fileVizVhd
    if(!$fileVizImage.Attached -or [IO.Path]::GetFullPath($fileVizImage.ImagePath) -ne $fileVizVhd){throw 'The dedicated test image did not attach correctly.'}
    $fileVizAttached=$true
    $fileVizDisk=$fileVizImage | Get-Disk
    $fileVizDisk | Select-Object Number,BusType,PartitionStyle,Path,FriendlyName | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fileVizOutput 'disk-identity.json')
    if($fileVizDisk.BusType -notin @('FileBackedVirtual','File Backed Virtual','Virtual') -or $fileVizDisk.PartitionStyle -ne 'RAW'){throw 'Refusing to initialize anything except the newly created raw file-backed virtual disk.'}
    $fileVizUsed=[IO.DriveInfo]::GetDrives() | ForEach-Object { $_.Name.Substring(0,1) }
    $fileVizLetter=@('Z','Y','X','W','V','U','T','S','R','Q') | Where-Object { $fileVizUsed -notcontains $_ -and !$ReservedLetters.Contains($_) } | Select-Object -First 1
    if(!$fileVizLetter){throw 'No unused test drive letter is available.'}
    $fileVizVerifiedDisk=(Get-DiskImage -ImagePath $fileVizVhd | Get-Disk)
    if($fileVizVerifiedDisk.Number -ne $fileVizDisk.Number){throw 'Disk identity changed before initialization.'}
    Initialize-Disk -Number $fileVizDisk.Number -PartitionStyle GPT
    $fileVizPartition=New-Partition -DiskNumber $fileVizDisk.Number -UseMaximumSize -DriveLetter $fileVizLetter
    if((Get-Partition -DriveLetter $fileVizLetter).DiskNumber -ne (Get-DiskImage -ImagePath $fileVizVhd | Get-Disk).Number){throw 'Partition is not on the dedicated image.'}
    Format-Volume -DriveLetter $fileVizLetter -FileSystem NTFS -NewFileSystemLabel FileVizTest -Confirm:$false | Out-Null
    $fileVizRoot=$fileVizLetter+':\'
    $fileVizData=Join-Path $fileVizRoot 'data'
    New-Item -ItemType Directory -Path $fileVizData,(Join-Path $fileVizData 'sub') | Out-Null
    for($fileVizIndex=0;$fileVizIndex -lt $FileCount;$fileVizIndex++){
        $fileVizFolder=Join-Path $fileVizData ('group-'+($fileVizIndex%100))
        [IO.Directory]::CreateDirectory($fileVizFolder) | Out-Null
        [IO.File]::WriteAllText((Join-Path $fileVizFolder ('file-'+$fileVizIndex+'.dat')),('fixture '+$fileVizIndex))
    }
    $fileVizKeeper=Join-Path $fileVizData 'hardlink-main.txt';[IO.File]::WriteAllText($fileVizKeeper,'hardlink fixture')
    for($fileVizIndex=0;$fileVizIndex -lt 100;$fileVizIndex++){& fsutil.exe hardlink create (Join-Path $fileVizData ('hardlink-'+$fileVizIndex+'.txt')) $fileVizKeeper | Out-Null}
    [IO.File]::WriteAllText(($fileVizKeeper+':named-stream'),'alternate stream fixture')
    [IO.File]::WriteAllText((Join-Path $fileVizData ('unicode-'+[char]0x6D4B+[char]0x91CF+'-'+[char]0xE9+'.txt')),'unicode fixture')
    $fileVizCompressed=Join-Path $fileVizData 'compressed.bin';[IO.File]::WriteAllBytes($fileVizCompressed,[byte[]]::new(1024*1024));& compact.exe /C /I $fileVizCompressed
    $fileVizSparse=Join-Path $fileVizData 'sparse.bin';& fsutil.exe file createnew $fileVizSparse 5242880;& fsutil.exe sparse setflag $fileVizSparse;& fsutil.exe sparse setrange $fileVizSparse 0 5242880
    $fileVizDeep=Join-Path $fileVizData 'sub';for($fileVizIndex=0;$fileVizIndex -lt 12;$fileVizIndex++){$fileVizDeep=Join-Path $fileVizDeep 'long-directory-name';[IO.Directory]::CreateDirectory($fileVizDeep) | Out-Null};[IO.File]::WriteAllText((Join-Path $fileVizDeep 'long-path.txt'),'long path fixture')
    & cmd.exe /c mklink /J (Join-Path $fileVizData 'junction') (Join-Path $fileVizData 'sub') | Out-Null
    $env:DOTNET_ROOT=Join-Path $fileVizWorkspace '.tools\dotnet'
    $fileVizDotnet=Join-Path $env:DOTNET_ROOT 'dotnet.exe'
    $fileVizBenchmark=Join-Path $fileVizWorkspace 'tools\FileViz.Benchmarks\bin\Release\net10.0-windows\FileViz.Benchmarks.dll'
    & $fileVizDotnet $fileVizBenchmark --parity $fileVizRoot (Join-Path $fileVizOutput 'parity')
    if($LASTEXITCODE -ne 0){throw 'Raw MFT parity failed; inspect the saved report.'}
    [IO.File]::WriteAllText((Join-Path $fileVizOutput 'completed.txt'),'Disposable NTFS parity passed.')
}
catch { $_ | Out-String | Set-Content -LiteralPath (Join-Path $fileVizOutput 'error.txt');throw }
finally { if($fileVizAttached){Dismount-DiskImage -ImagePath $fileVizVhd};Stop-Transcript | Out-Null }