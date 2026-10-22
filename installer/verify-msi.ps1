param([Parameter(Mandatory)][string]$Msi,[Parameter(Mandatory)][string]$Payload)
$ErrorActionPreference='Stop'
$Msi=(Resolve-Path $Msi).Path;$Payload=(Resolve-Path $Payload).Path
$installer=New-Object -ComObject WindowsInstaller.Installer
$db=$installer.OpenDatabase($Msi,0)
function Check([bool]$condition,[string]$label){if(-not $condition){throw "MSI verification failed: $label"};Write-Output "PASS MSI $label"}
function Rows([string]$sql){
    $view=$db.OpenView($sql);$view.Execute() | Out-Null
    try{while($r=$view.Fetch()){$values=@();for($i=1;$i -le $r.FieldCount();$i++){$values+=$r.StringData($i)};[pscustomobject]@{Values=$values};[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($r)}}
    finally{$view.Close() | Out-Null;[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)}
}
$properties=@{};Rows 'SELECT * FROM `Property`' | ForEach-Object {$properties[$_.Values[0]]=$_.Values[1]}
Check ($properties.ProductVersion -eq '0.1.0') 'version'
Check ($properties.ProductName -like 'App Proxy Bridge*') 'product identity'
$fileRows=@(Rows 'SELECT `File`,`FileName`,`FileSize`,`Component_`,`Sequence` FROM `File`')
$files=@(Get-ChildItem $Payload -File)
Check ($fileRows.Count -eq $files.Count) 'payload file count'
foreach($row in $fileRows){$name=$row.Values[1].Split('|')[-1];$file=Get-Item -LiteralPath (Join-Path $Payload $name);Check ($file.Length -eq [long]$row.Values[2]) ("file size "+$name)}
Check (@($fileRows | Where-Object {$_.Values[1].Split('|')[-1] -eq 'profiles.json'}).Count -eq 0) 'no user configuration'
$services=@(Rows 'SELECT `Name`,`ServiceType`,`StartType`,`StartName`,`Component_` FROM `ServiceInstall`')
Check ($services.Count -eq 1 -and $services[0].Values[0] -eq 'AppProxyBridge' -and $services[0].Values[1] -eq '16' -and $services[0].Values[2] -eq '2') 'single automatic standby service'
$control=@(Rows 'SELECT `Name`,`Event`,`Wait` FROM `ServiceControl`')
Check ($control.Count -eq 1 -and $control[0].Values[1] -eq '163' -and $control[0].Values[2] -eq '1') 'service stop and uninstall lifecycle'
$registry=@(Rows 'SELECT `Root`,`Key`,`Name`,`Value` FROM `Registry`')
Check ($registry.Count -eq 1 -and $registry[0].Values[1] -eq 'SOFTWARE\AppProxyBridge' -and $registry[0].Values[2] -eq 'OwnerSid' -and $registry[0].Values[3] -eq '[UserSID]') 'installation owner SID only'
$actions=@(Rows 'SELECT `Action`,`Type`,`Target` FROM `CustomAction`')
Check ($actions.Count -eq 3 -and @($actions | Where-Object {$_.Values[2] -notin @('--configure','--cleanup')}).Count -eq 0) 'restricted installer actions'
$admin=@(Rows 'SELECT `Action` FROM `AdminExecuteSequence`')
Check (@($admin | Where-Object {$_.Values[0] -match 'Service|Firewall'}).Count -eq 0) 'administrative extraction has no service actions'
$directory=@(Rows 'SELECT `Directory_Parent` FROM `Directory` WHERE `Directory` = ''INSTALLDIR''')
Check ($directory[0].Values[0] -eq 'ProgramFiles64Folder') 'protected x64 install location'
$view=$db.OpenView('SELECT `Data` FROM `_Streams` WHERE `Name` = ''app.cab''');$view.Execute();$stream=$view.Fetch()
Check ($stream.DataSize(1) -gt 1024*1024) 'embedded cabinet'
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($stream);$view.Close();[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db);[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
Write-Output 'MSI static verification complete. This does not replace clean-machine installation acceptance.'
