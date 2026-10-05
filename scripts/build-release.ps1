param([string]$Version='0.1.0',[string]$OutputDirectory='artifacts\release',[string]$Iscc='')
$ErrorActionPreference='Stop'
$fileVizRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $fileVizRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$fileVizDotnet=Join-Path $fileVizRoot '.tools\dotnet\dotnet.exe'
if(!(Test-Path -LiteralPath $fileVizDotnet)){$fileVizDotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$fileVizOutput=[IO.Path]::GetFullPath((Join-Path $fileVizRoot $OutputDirectory))
if(!$fileVizOutput.StartsWith($fileVizRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Release output must be inside the workspace.'}
if((Test-Path -LiteralPath $fileVizOutput) -and (Get-ChildItem -LiteralPath $fileVizOutput -Force).Count -gt 0){throw 'Use an empty output directory; existing releases are never removed automatically.'}
$fileVizPortable=Join-Path $fileVizOutput 'portable';New-Item -ItemType Directory -Force -Path $fileVizPortable | Out-Null
& $fileVizDotnet publish src/FileViz.App -c Release -r win-x64 --self-contained true -o $fileVizPortable -p:Version=$Version
if($LASTEXITCODE -ne 0){throw 'Desktop publish failed.'}
& $fileVizDotnet publish src/FileViz.Worker -c Release -r win-x64 --self-contained true -o (Join-Path $fileVizPortable 'worker') -p:Version=$Version
if($LASTEXITCODE -ne 0){throw 'Worker publish failed.'}
Copy-Item -LiteralPath LICENSE,README.md,CHANGELOG.md -Destination $fileVizPortable
Copy-Item -LiteralPath docs -Destination (Join-Path $fileVizPortable 'docs') -Recurse
$fileVizNotices=Join-Path (Split-Path $fileVizDotnet) 'ThirdPartyNotices.txt'
if(Test-Path -LiteralPath $fileVizNotices){Copy-Item -LiteralPath $fileVizNotices -Destination (Join-Path $fileVizPortable 'DOTNET-THIRD-PARTY-NOTICES.txt')}
Copy-Item -LiteralPath THIRD-PARTY-NOTICES.md -Destination $fileVizPortable
Get-ChildItem -LiteralPath packaging -File | Where-Object { $_.Name -like '*LICENSE.txt' -or $_.Name -like '*THIRD-PARTY-NOTICES.txt' } | Copy-Item -Destination $fileVizPortable
$fileVizZip=Join-Path $fileVizOutput ('FileViz-'+$Version+'-win-x64-portable.zip')
Compress-Archive -Path (Join-Path $fileVizPortable '*') -DestinationPath $fileVizZip -CompressionLevel Optimal
if(!$Iscc){$Iscc=Join-Path $fileVizRoot '.tools\inno\ISCC.exe'}
if(!(Test-Path -LiteralPath $Iscc)){$fileVizCompiler=Get-Command ISCC.exe -ErrorAction SilentlyContinue;if($fileVizCompiler){$Iscc=$fileVizCompiler.Source}}
if(!(Test-Path -LiteralPath $Iscc)){throw 'Install Inno Setup 6.7.3 and supply -Iscc, or place it in .tools\inno.'}
& $Iscc ('/DPackageDir='+$fileVizPortable) ('/DAppVersion='+$Version) ('/DOutputDir='+$fileVizOutput) packaging/FileViz.iss
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
Get-ChildItem -LiteralPath $fileVizOutput -File | Where-Object { $_.Extension -in '.zip','.exe' } | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.Name } | Set-Content -LiteralPath (Join-Path $fileVizOutput 'SHA256SUMS.txt') -Encoding ascii
Write-Output $fileVizOutput