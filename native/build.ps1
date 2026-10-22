param([switch]$Tests)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$zig=Join-Path $root '.tools\zig-x86_64-windows-0.14.1\zig.exe'
if(-not(Test-Path $zig)){throw 'Run bootstrap.ps1 first (verified portable Zig compiler required).'}
$out=Join-Path $root 'artifacts\native'
New-Item -ItemType Directory $out -Force | Out-Null
$env:ZIG_GLOBAL_CACHE_DIR=Join-Path $root '.cache\zig'
$env:ZIG_LOCAL_CACHE_DIR=Join-Path $root '.cache\zig-local'
$env:TEMP=Join-Path $root '.cache\tmp'
$env:TMP=$env:TEMP
New-Item -ItemType Directory $env:TEMP -Force | Out-Null
$src=Join-Path $root 'vendor\ProxyBridge\Windows\src'
$sources=Get-ChildItem $src -Filter '*.c' | ForEach-Object FullName
$argsList=@('cc','-target','x86_64-windows-gnu','-O2','-shared','-DPROXYBRIDGE_EXPORTS','-DWINDIVERTEXPORT=extern','-D_CRT_SECURE_NO_WARNINGS',('-I'+$src),('-I'+(Join-Path $root 'vendor\WinDivert')))
$argsList+=$sources
$argsList+=@( (Join-Path $PSScriptRoot 'windivert-loader.c'),(Join-Path $PSScriptRoot 'apb.c'),'-lws2_32','-liphlpapi','-ladvapi32','-lpsapi','-o',(Join-Path $out 'AppProxyBridgeNative.dll'))
& $zig @argsList
if($LASTEXITCODE){throw 'Native build failed.'}
Write-Output (Join-Path $out 'AppProxyBridgeNative.dll')
