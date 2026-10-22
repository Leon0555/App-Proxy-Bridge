param([switch]$FetchWinDivert)
$ErrorActionPreference='Stop'
$tools=Join-Path $PSScriptRoot '.tools'
New-Item -ItemType Directory $tools -Force | Out-Null
$zig=Join-Path $tools 'zig-x86_64-windows-0.14.1\zig.exe'
if(-not(Test-Path -LiteralPath $zig)){
    $zip=Join-Path $tools 'zig-full.zip'
    & curl.exe -fLsS --retry 3 --connect-timeout 15 --max-time 600 'https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip' -o $zip
    if($LASTEXITCODE){throw 'Compiler download failed.'}
    if((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne '554f5378228923ffd558eac35e21af020c73789d87afeabf4bfd16f2e6feed2c'){throw 'Compiler SHA256 mismatch.'}
    Expand-Archive -LiteralPath $zip -DestinationPath $tools -Force
}
if($FetchWinDivert){
    $zip=Join-Path $tools 'windivert.zip'
    & curl.exe -fLsS --retry 2 --connect-timeout 15 --max-time 90 -H 'Accept: application/octet-stream' 'https://api.github.com/repos/basil00/WinDivert/releases/assets/78510020' -o $zip
    if($LASTEXITCODE){throw 'Official WinDivert SDK download failed.'}
    Expand-Archive -LiteralPath $zip -DestinationPath $tools -Force
    $sys=Join-Path $tools 'WinDivert-2.2.2-A\x64\WinDivert64.sys'
    if((Get-AuthenticodeSignature -LiteralPath $sys).Status -ne 'Valid'){throw 'Official driver signature is not valid. Do not package.'}
}
Write-Output 'Build tools ready. No driver or service has been installed.'
