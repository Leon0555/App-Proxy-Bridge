param([Parameter(Mandatory)][string]$Payload,[switch]$Preview)
$ErrorActionPreference='Stop'
$Payload=(Resolve-Path -LiteralPath $Payload).Path
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $root 'release'
$version='0.1.0'
$label=if($Preview){'App Proxy Bridge Developer Preview'}else{'App Proxy Bridge'}
$msi=Join-Path $out ("App-Proxy-Bridge-$version-win-x64"+$(if($Preview){'-preview'})+'.msi')
$work=Join-Path $root 'artifacts\installer'
New-Item -ItemType Directory $work -Force | Out-Null
$files=@(Get-ChildItem -LiteralPath $Payload -File | Sort-Object Name)
if(-not($files.Name -contains 'AppProxyBridge.Service.exe') -or -not($files.Name -contains 'App Proxy Bridge.exe')){throw 'Publish the applications first.'}
if(-not $Preview -and -not($files.Name -contains 'WinDivert64.sys')){throw 'A production installer requires the official signed driver.'}
$cab=Join-Path $work 'app.cab'
$ddf=Join-Path $work 'app.ddf'
$lines=@('.OPTION EXPLICIT','.Set CabinetNameTemplate=app.cab',('.Set DiskDirectoryTemplate="'+$work+'"'),'.Set CompressionType=LZX','.Set Cabinet=ON','.Set Compress=ON','.Set MaxDiskSize=0')
$ids=@{};$index=0
foreach($file in $files){$index++;$id='F'+$index.ToString('0000');$ids[$file.Name]=$id;$lines+=('"'+$file.FullName+'" '+$id)}
$lines | Set-Content -LiteralPath $ddf -Encoding utf8
$manifest=($files | ForEach-Object {$_.Name+':'+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}) -join "`n"
$manifestPath=Join-Path $work 'cabinet.sha256'
if(-not(Test-Path $cab) -or -not(Test-Path $manifestPath) -or [IO.File]::ReadAllText($manifestPath) -ne $manifest){
    & (Join-Path $env:WINDIR 'System32\makecab.exe') /F $ddf | Out-Null
    if($LASTEXITCODE -or -not(Test-Path $cab)){throw 'Cabinet build failed.'}
    [IO.File]::WriteAllText($manifestPath,$manifest)
}
if(Test-Path -LiteralPath $msi){Remove-Item -LiteralPath $msi -Force}
$installer=New-Object -ComObject WindowsInstaller.Installer
function Invoke-Com($obj,[string]$name,[string]$kind,[object[]]$values){
    try{
        switch($name){
            'OpenDatabase' {$obj.OpenDatabase([string]$values[0],[int]$values[1])}
            'OpenView' {$obj.OpenView([string]$values[0])}
            'CreateRecord' {$obj.CreateRecord([int]$values[0])}
            'IntegerData' {$obj.IntegerData([int]$values[0])=[int]$values[1]}
            'StringData' {$obj.StringData([int]$values[0])=[string]$values[1]}
            'Execute' {if($values.Count){$obj.Execute($values[0])}else{$obj.Execute()}}
            'Close' {$obj.Close()}
            'Commit' {$obj.Commit()}
            'Persist' {$obj.Persist()}
            'SetStream' {$obj.SetStream([int]$values[0],[string]$values[1])}
            'SummaryInformation' {$obj.SummaryInformation([int]$values[0])}
            'Property' {$obj.Property([int]$values[0])=$values[1]}
            default {throw "Unsupported COM operation: $name"}
        }
    }
    catch{throw ("MSI COM "+$name+" ("+$kind+"): "+$_.Exception.Message+"; "+$(if($name -eq 'OpenView'){$values[0]})+"; argument types: "+(($values | ForEach-Object {if($null -eq $_){'null'}else{$_.GetType().Name}})-join ','))}
}
$db=Invoke-Com $installer 'OpenDatabase' 'InvokeMethod' @($msi,3)
$script:schemas=@{}
function Sql([string]$query){
    if($query -match '^CREATE TABLE ([A-Za-z_]+) '){$tableName=$Matches[1];$script:schemas[$tableName]=@([regex]::Matches($query,'\b([A-Za-z_][A-Za-z_0-9]*)\s+(?:CHAR|SHORT|LONG|OBJECT)\b') | ForEach-Object {$_.Groups[1].Value})}
    $q=[string][char]96
    $query=[regex]::Replace($query,'[A-Za-z_][A-Za-z_0-9]*',{param($m) if($m.Value -cin @('CREATE','TABLE','CHAR','SHORT','LONG','OBJECT','NOT','NULL','PRIMARY','KEY')){$m.Value}else{$q+$m.Value+$q}})
    $view=Invoke-Com $db 'OpenView' 'InvokeMethod' @($query)
    try{Invoke-Com $view 'Execute' 'InvokeMethod' @() | Out-Null}finally{Invoke-Com $view 'Close' 'InvokeMethod' @() | Out-Null;[void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)}
}
function Row([string]$table,[object[]]$values){
    $record=Invoke-Com $installer 'CreateRecord' 'InvokeMethod' @($values.Count)
    for($i=0;$i -lt $values.Count;$i++){
        if($null -ne $values[$i]){if($values[$i] -is [int]){Invoke-Com $record 'IntegerData' 'SetProperty' @(($i+1),$values[$i]) | Out-Null}else{Invoke-Com $record 'StringData' 'SetProperty' @(($i+1),[string]$values[$i]) | Out-Null}}
    }
    $marks=(@('?')*$values.Count)-join ','
    $q=[string][char]96;$columns=($script:schemas[$table] | ForEach-Object {$q+$_+$q}) -join ','
    $view=Invoke-Com $db 'OpenView' 'InvokeMethod' @("INSERT INTO "+$q+$table+$q+" ("+$columns+") VALUES ("+$marks+")")
    try{Invoke-Com $view 'Execute' 'InvokeMethod' @($record) | Out-Null}finally{Invoke-Com $view 'Close' 'InvokeMethod' @() | Out-Null;[void][Runtime.InteropServices.Marshal]::ReleaseComObject($view);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($record)}
}
function StreamRow([string]$table,[string]$name,[string]$path){
    $record=Invoke-Com $installer 'CreateRecord' 'InvokeMethod' @(2)
    Invoke-Com $record 'StringData' 'SetProperty' @(1,$name) | Out-Null
    Invoke-Com $record 'SetStream' 'InvokeMethod' @(2,$path) | Out-Null
    $q=[string][char]96
    $view=Invoke-Com $db 'OpenView' 'InvokeMethod' @("INSERT INTO "+$q+$table+$q+" ("+$q+"Name"+$q+","+$q+"Data"+$q+") VALUES (?,?)")
    try{Invoke-Com $view 'Execute' 'InvokeMethod' @($record) | Out-Null}finally{Invoke-Com $view 'Close' 'InvokeMethod' @() | Out-Null;[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view);[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)}
}
Sql 'CREATE TABLE Property (Property CHAR(72) NOT NULL, Value CHAR(0) NOT NULL PRIMARY KEY Property)'
Sql 'CREATE TABLE Directory (Directory CHAR(72) NOT NULL, Directory_Parent CHAR(72), DefaultDir CHAR(255) NOT NULL PRIMARY KEY Directory)'
Sql 'CREATE TABLE Component (Component CHAR(72) NOT NULL, ComponentId CHAR(38), Directory_ CHAR(72) NOT NULL, Attributes SHORT NOT NULL, Condition CHAR(255), KeyPath CHAR(72) PRIMARY KEY Component)'
Sql 'CREATE TABLE Feature (Feature CHAR(38) NOT NULL, Feature_Parent CHAR(38), Title CHAR(64), Description CHAR(255), Display SHORT, Level SHORT NOT NULL, Directory_ CHAR(72), Attributes SHORT NOT NULL PRIMARY KEY Feature)'
Sql 'CREATE TABLE FeatureComponents (Feature_ CHAR(38) NOT NULL, Component_ CHAR(72) NOT NULL PRIMARY KEY Feature_, Component_)'
Sql 'CREATE TABLE File (File CHAR(72) NOT NULL, Component_ CHAR(72) NOT NULL, FileName CHAR(255) NOT NULL, FileSize LONG NOT NULL, Version CHAR(72), Language CHAR(20), Attributes SHORT, Sequence LONG NOT NULL PRIMARY KEY File)'
Sql 'CREATE TABLE Media (DiskId SHORT NOT NULL, LastSequence LONG NOT NULL, DiskPrompt CHAR(64), Cabinet CHAR(255), VolumeLabel CHAR(32), Source CHAR(72) PRIMARY KEY DiskId)'
Sql 'CREATE TABLE Registry (Registry CHAR(72) NOT NULL, Root SHORT NOT NULL, Key CHAR(255) NOT NULL, Name CHAR(255), Value CHAR(0), Component_ CHAR(72) NOT NULL PRIMARY KEY Registry)'
Sql 'CREATE TABLE Shortcut (Shortcut CHAR(72) NOT NULL, Directory_ CHAR(72) NOT NULL, Name CHAR(128) NOT NULL, Component_ CHAR(72) NOT NULL, Target CHAR(72) NOT NULL, Arguments CHAR(255), Description CHAR(255), Hotkey SHORT, Icon_ CHAR(72), IconIndex SHORT, ShowCmd SHORT, WkDir CHAR(72) PRIMARY KEY Shortcut)'
Sql 'CREATE TABLE RemoveFile (FileKey CHAR(72) NOT NULL, Component_ CHAR(72) NOT NULL, FileName CHAR(255), DirProperty CHAR(72) NOT NULL, InstallMode SHORT NOT NULL PRIMARY KEY FileKey)'
Sql 'CREATE TABLE ServiceInstall (ServiceInstall CHAR(72) NOT NULL, Name CHAR(255) NOT NULL, DisplayName CHAR(255), ServiceType LONG NOT NULL, StartType LONG NOT NULL, ErrorControl LONG NOT NULL, LoadOrderGroup CHAR(255), Dependencies CHAR(255), StartName CHAR(255), Password CHAR(255), Arguments CHAR(255), Component_ CHAR(72) NOT NULL, Description CHAR(255) PRIMARY KEY ServiceInstall)'
Sql 'CREATE TABLE ServiceControl (ServiceControl CHAR(72) NOT NULL, Name CHAR(255) NOT NULL, Event SHORT NOT NULL, Arguments CHAR(255), Wait SHORT, Component_ CHAR(72) NOT NULL PRIMARY KEY ServiceControl)'
Sql 'CREATE TABLE CustomAction (Action CHAR(72) NOT NULL, Type SHORT NOT NULL, Source CHAR(72), Target CHAR(0), ExtendedType LONG PRIMARY KEY Action)'
Sql 'CREATE TABLE InstallExecuteSequence (Action CHAR(72) NOT NULL, Condition CHAR(255), Sequence SHORT PRIMARY KEY Action)'
Sql 'CREATE TABLE InstallUISequence (Action CHAR(72) NOT NULL, Condition CHAR(255), Sequence SHORT PRIMARY KEY Action)'
Sql 'CREATE TABLE AdminExecuteSequence (Action CHAR(72) NOT NULL, Condition CHAR(255), Sequence SHORT PRIMARY KEY Action)'
Sql 'CREATE TABLE AdminUISequence (Action CHAR(72) NOT NULL, Condition CHAR(255), Sequence SHORT PRIMARY KEY Action)'
Sql 'CREATE TABLE Upgrade (UpgradeCode CHAR(38) NOT NULL, VersionMin CHAR(20), VersionMax CHAR(20), Language CHAR(255), Attributes LONG NOT NULL, Remove CHAR(255), ActionProperty CHAR(72) NOT NULL PRIMARY KEY UpgradeCode, VersionMin, VersionMax, Language, Attributes)'
Sql 'CREATE TABLE LaunchCondition (Condition CHAR(255) NOT NULL, Description CHAR(255) NOT NULL PRIMARY KEY Condition)'
Sql 'CREATE TABLE Icon (Name CHAR(72) NOT NULL, Data OBJECT NOT NULL PRIMARY KEY Name)'
Sql 'CREATE TABLE AppSearch (Property CHAR(72) NOT NULL, Signature_ CHAR(72) NOT NULL PRIMARY KEY Property, Signature_)'
Sql 'CREATE TABLE RegLocator (Signature_ CHAR(72) NOT NULL, Root SHORT NOT NULL, Key CHAR(255) NOT NULL, Name CHAR(255), Type SHORT PRIMARY KEY Signature_)'
$upgrade='{4C0BC833-3DC9-443B-9A01-84F3B7166E69}'
foreach($entry in @(
    @('ProductCode',('{'+[guid]::NewGuid().ToString().ToUpperInvariant()+'}')),
    @('UpgradeCode',$upgrade),@('ProductVersion',$version),@('ProductLanguage','1033'),@('Manufacturer','App Proxy Bridge contributors'),
    @('ProductName',$label),@('ALLUSERS','1'),@('INSTALLLEVEL','1'),@('ARPNOMODIFY','1'),@('ARPNOREPAIR','1'),
    @('SecureCustomProperties','APB_OLDPRODUCTS;APB_NEWERPRODUCTS')
)){Row 'Property' $entry}
foreach($entry in @(@('TARGETDIR',$null,'SourceDir'),@('ProgramFiles64Folder','TARGETDIR','.'),@('INSTALLDIR','ProgramFiles64Folder','AppProxyBridge'),@('ProgramMenuFolder','TARGETDIR','.'),@('APBMenu','ProgramMenuFolder','App Proxy Bridge'))){Row 'Directory' $entry}
Row 'Feature' @('Main',$null,$label,'Application and background service',1,1,'INSTALLDIR',0)
$i=0
foreach($file in $files){
    $i++;$id=$ids[$file.Name];$component='C'+$id
    $hash=[Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('AppProxyBridge/'+$file.Name))
    $guid='{'+([guid]::new([byte[]]$hash[0..15])).ToString().ToUpperInvariant()+'}'
    Row 'Component' @($component,$guid,'INSTALLDIR',256,$null,$id)
    Row 'FeatureComponents' @('Main',$component)
    $short='APB'+$i.ToString('0000')+'.BIN'
    Row 'File' @($id,$component,($short+'|'+$file.Name),[int]$file.Length,$null,$null,512,$i)
}
$service=$ids['AppProxyBridge.Service.exe'];$serviceComponent='C'+$service;$app=$ids['App Proxy Bridge.exe']
Row 'Registry' @('OwnerSid',2,'SOFTWARE\AppProxyBridge','OwnerSid','[UserSID]',$serviceComponent)
Row 'ServiceInstall' @('Service','AppProxyBridge','App Proxy Bridge',16,2,32769,$null,$null,'LocalSystem',$null,$null,$serviceComponent,'Per-application proxy engine. Starts in standby; activation is manual.')
Row 'ServiceControl' @('Control','AppProxyBridge',163,$null,1,$serviceComponent)
Row 'Shortcut' @('MainShortcut','APBMenu','APB|App Proxy Bridge',('C'+$app),('[#'+$app+']'),$null,$label,$null,'AppIcon',0,1,'INSTALLDIR')
Row 'RemoveFile' @('MenuFolder',('C'+$app),$null,'APBMenu',2)
Row 'CustomAction' @('ConfigureFirewall',3090,$service,'--configure',$null)
Row 'CustomAction' @('RollbackFirewall',3346,$service,'--cleanup',$null)
Row 'CustomAction' @('CleanupFirewall',3090,$service,'--cleanup',$null)
Row 'Upgrade' @($upgrade,'0.0.0',$version,$null,769,$null,'APB_OLDPRODUCTS')
Row 'Upgrade' @($upgrade,$version,$null,$null,2,$null,'APB_NEWERPRODUCTS')
Row 'LaunchCondition' @('NOT APB_NEWERPRODUCTS','A newer App Proxy Bridge version is installed.')
Row 'RegLocator' @('WindowsBuild',2,'SOFTWARE\Microsoft\Windows NT\CurrentVersion','CurrentBuildNumber',18)
Row 'AppSearch' @('APB_WINDOWSBUILD','WindowsBuild')
Row 'LaunchCondition' @('VersionNT64 AND APB_WINDOWSBUILD >= 10240','Windows 10/11 x64 is required.')
foreach($entry in @(
 @('FindRelatedProducts',$null,25),@('AppSearch',$null,50),@('LaunchConditions',$null,100),@('CostInitialize',$null,800),@('FileCost',$null,900),@('CostFinalize',$null,1000),
 @('InstallValidate',$null,1400),@('InstallInitialize',$null,1500),@('RemoveExistingProducts','APB_OLDPRODUCTS',1550),@('ProcessComponents',$null,1600),
 @('UnpublishFeatures',$null,1800),@('StopServices',$null,1900),@('DeleteServices',$null,2000),@('CleanupFirewall','REMOVE="ALL"',2050),
 @('RemoveRegistryValues',$null,2600),@('RemoveShortcuts',$null,3200),@('RemoveFiles',$null,3500),@('CreateFolders',$null,3700),
 @('InstallFiles',$null,4000),@('CreateShortcuts',$null,4500),@('WriteRegistryValues',$null,5000),@('InstallServices',$null,5800),
 @('RollbackFirewall','NOT REMOVE',5890),@('ConfigureFirewall','NOT REMOVE',5900),@('StartServices',$null,6000),
 @('RegisterProduct',$null,6100),@('PublishFeatures',$null,6300),@('PublishProduct',$null,6400),@('InstallFinalize',$null,6600)
)){Row 'InstallExecuteSequence' $entry}
foreach($entry in @(@('FindRelatedProducts',$null,25),@('AppSearch',$null,50),@('LaunchConditions',$null,100),@('CostInitialize',$null,800),@('FileCost',$null,900),@('CostFinalize',$null,1000),@('ExecuteAction',$null,1300))){Row 'InstallUISequence' $entry}
foreach($entry in @(@('CostInitialize',$null,800),@('FileCost',$null,900),@('CostFinalize',$null,1000),@('InstallValidate',$null,1400),@('InstallInitialize',$null,1500),@('InstallAdminPackage',$null,3900),@('InstallFiles',$null,4000),@('InstallFinalize',$null,6600))){Row 'AdminExecuteSequence' $entry}
foreach($entry in @(@('CostInitialize',$null,800),@('FileCost',$null,900),@('CostFinalize',$null,1000),@('ExecuteAction',$null,1300))){Row 'AdminUISequence' $entry}
Row 'Media' @(1,$files.Count,$null,'#app.cab',$null,$null)
StreamRow '_Streams' 'app.cab' $cab
StreamRow 'Icon' 'AppIcon' (Join-Path $root 'AppProxyBridge.App\Assets\app.ico')
$summary=Invoke-Com $db 'SummaryInformation' 'GetProperty' @(20)
foreach($entry in @(@(2,$label),@(3,'Windows x64 per-application proxy manager'),@(4,'App Proxy Bridge contributors'),@(7,'x64;1033'),@(9,('{'+[guid]::NewGuid().ToString().ToUpperInvariant()+'}')),@(14,500),@(15,2))){
    Invoke-Com $summary 'Property' 'SetProperty' $entry | Out-Null
}
Invoke-Com $summary 'Persist' 'InvokeMethod' @() | Out-Null
Invoke-Com $db 'Commit' 'InvokeMethod' @() | Out-Null
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
$summary=$null;$db=$null;$installer=$null
[GC]::Collect();[GC]::WaitForPendingFinalizers()
$hash=Get-FileHash -LiteralPath $msi -Algorithm SHA256
("$($hash.Hash.ToLowerInvariant())  "+[IO.Path]::GetFileName($msi)) | Set-Content -LiteralPath (Join-Path $out 'SHA256SUMS.txt') -Encoding ascii
Write-Output $msi
