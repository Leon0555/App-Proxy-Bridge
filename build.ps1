param([switch]$Test,[switch]$UiTest,[switch]$Installed,[switch]$Publish,[switch]$Installer,[switch]$Preview,[switch]$Smoke)
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_NOLOGO='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
if($Installed){$UiTest=$true}
if($Smoke){$Publish=$true}
$sdk=Join-Path (Split-Path $PSScriptRoot -Parent) '.dotnet\dotnet.exe'
if(-not(Test-Path $sdk)){$sdk=(Get-Command dotnet).Source}
$env:DOTNET_CLI_HOME=Join-Path $PSScriptRoot '.cache\dotnet'
$env:DOTNET_ROOT=Split-Path $sdk -Parent
function Dotnet([string[]]$Arguments){& $sdk @Arguments;if($LASTEXITCODE){throw "dotnet failed: $($Arguments[0])"}}
& (Join-Path $PSScriptRoot 'native\build.ps1')
if($LASTEXITCODE){throw 'Native build failed'}
foreach($project in @('App','Service','Tests','UiTests')){Dotnet @('build',(Join-Path $PSScriptRoot "AppProxyBridge.$project\AppProxyBridge.$project.csproj"),'-c','Release','--nologo')}
if($Test){
    $env:APB_NATIVE=Join-Path $PSScriptRoot 'artifacts\native\AppProxyBridgeNative.dll'
    Dotnet @('run','--project',(Join-Path $PSScriptRoot 'AppProxyBridge.Tests\AppProxyBridge.Tests.csproj'),'-c','Release','--no-build')
}
if($UiTest){$uiArgs=@('run','--project',(Join-Path $PSScriptRoot 'AppProxyBridge.UiTests\AppProxyBridge.UiTests.csproj'),'-c','Release','--no-build','--',(Join-Path $PSScriptRoot 'artifacts\previews'));if($Installed){$uiArgs+='--installed'};Dotnet $uiArgs}
if($Publish -or $Installer){
    $out=Join-Path $PSScriptRoot 'release\app'
    foreach($project in @('App','Service')){Dotnet @('publish',(Join-Path $PSScriptRoot "AppProxyBridge.$project\AppProxyBridge.$project.csproj"),'-c','Release','-r','win-x64','--self-contained','true','--ignore-failed-sources','-p:NuGetAudit=false','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:DebugType=None','-p:DebugSymbols=false','-o',$out)}
    Copy-Item (Join-Path $PSScriptRoot 'artifacts\native\AppProxyBridgeNative.dll') $out -Force
    foreach($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','VERIFICATION.md')){Copy-Item (Join-Path $PSScriptRoot $name) $out -Force}
    Copy-Item (Join-Path $PSScriptRoot 'vendor\WinDivert\LICENSE') (Join-Path $out 'LICENSE-WinDivert.txt') -Force
    Copy-Item (Join-Path $PSScriptRoot 'vendor\ProxyBridge\LICENSE') (Join-Path $out 'LICENSE-ProxyBridge.txt') -Force
    foreach($name in @('LICENSE.txt','ThirdPartyNotices.txt')){if(Test-Path (Join-Path $env:DOTNET_ROOT $name)){Copy-Item (Join-Path $env:DOTNET_ROOT $name) (Join-Path $out ('DOTNET-'+$name)) -Force}}
    $wd=Join-Path $PSScriptRoot '.tools\WinDivert-2.2.2-A\x64'
    if((Test-Path (Join-Path $wd 'WinDivert.dll')) -and (Test-Path (Join-Path $wd 'WinDivert64.sys'))){
        $sig=Get-AuthenticodeSignature (Join-Path $wd 'WinDivert64.sys');if($sig.Status -ne 'Valid'){throw 'WinDivert driver signature is not valid.'}
        foreach($name in @('WinDivert.dll','WinDivert64.sys')){Copy-Item (Join-Path $wd $name) $out -Force}
    }elseif(-not $Preview){throw 'Official signed WinDivert runtime unavailable. Production packaging stopped. -Preview builds an explicitly incomplete developer preview.'}
    if($Preview){'DEVELOPER PREVIEW: WinDivert runtime may be absent. Traffic interception has not been accepted. See VERIFICATION.md.' | Set-Content (Join-Path $out 'DEVELOPER-PREVIEW.txt')}
    if($Installer){& (Join-Path $PSScriptRoot 'installer\build-msi.ps1') -Payload $out -Preview:$Preview;& (Join-Path $PSScriptRoot 'installer\verify-msi.ps1') -Msi (Join-Path $PSScriptRoot ('release\App-Proxy-Bridge-0.1.0-win-x64'+$(if($Preview){'-preview'})+'.msi')) -Payload $out}
    if($Smoke){Dotnet @('run','--project',(Join-Path $PSScriptRoot 'AppProxyBridge.UiTests\AppProxyBridge.UiTests.csproj'),'-c','Release','--no-build','--','--published',$out)}
}
