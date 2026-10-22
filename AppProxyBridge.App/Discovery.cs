using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AppProxyBridge.Core;

namespace AppProxyBridge.App;
public sealed record Candidate(string Name,AppComponent Component)
{
    public string Path=>Component.Path;
    public override string ToString()=>Name+"  ·  "+Path;
}
public static class Discovery
{
    static string Literal(string s)=>"'"+s.Replace("'","''")+"'";
    static async Task<string> Query(string code)
    {
        var info=new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
        foreach(var arg in new[]{"-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding=[Text.UTF8Encoding]::new();$ErrorActionPreference='Stop';"+code))})info.ArgumentList.Add(arg);
        using var p=Process.Start(info)??throw new IOException("无法查询应用。");var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();
        try{await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));}catch{p.Kill(true);throw new IOException("应用查询超时。");}
        await errors;if(p.ExitCode!=0)throw new IOException("系统应用查询失败，请手动选择 EXE。");return(await output).Trim();
    }
    static T[] Parse<T>(string json)=>string.IsNullOrWhiteSpace(json)?[]:JsonSerializer.Deserialize<T[]>(json)??[];
    public static async Task<AppComponent> Identify(string path)
    {
        path=System.IO.Path.GetFullPath(path);
        if(!path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(path))throw new IOException("请选择存在的 EXE 文件。");
        if(!path.Contains(@"\WindowsApps\",StringComparison.OrdinalIgnoreCase))return new(){Path=path};
        string json=await Query("$x="+Literal(path)+";$r=@(Get-AppxPackage | Where-Object {$_.InstallLocation -and $x.StartsWith($_.InstallLocation+'\\',[StringComparison]::OrdinalIgnoreCase)} | ForEach-Object {$p=$_;[xml]$m=Get-Content -LiteralPath (Join-Path $p.InstallLocation 'AppxManifest.xml') -Raw;$a=$m.Package.Applications.Application | Where-Object {$_.Executable -and (Join-Path $p.InstallLocation $_.Executable) -eq $x} | Select-Object -First 1;[pscustomobject]@{Path=$x;PackageFamily=$p.PackageFamilyName;PackageRelativePath=$x.Substring($p.InstallLocation.Length+1);ApplicationId=$(if($a){[string]$a.Id}else{''})}});ConvertTo-Json -InputObject $r -Compress");
        return Parse<AppComponent>(json).FirstOrDefault()??throw new IOException("无法识别此打包应用的稳定身份，请从开始菜单列表添加。");
    }
    public static async Task<string> Resolve(AppComponent c)
    {
        if(c.PackageFamily.Length==0){if(!File.Exists(c.Path))throw new IOException("路径失效："+c.Path);return System.IO.Path.GetFullPath(c.Path);}
        if(System.IO.Path.IsPathRooted(c.PackageRelativePath)||c.PackageRelativePath.Split(['\\','/']).Contains(".."))throw new IOException("包内路径无效。");
        string path=await Query("$p=Get-AppxPackage | Where-Object PackageFamilyName -eq "+Literal(c.PackageFamily)+" | Sort-Object Version -Descending | Select-Object -First 1;if($p){Join-Path $p.InstallLocation "+Literal(c.PackageRelativePath)+"}");
        if(!File.Exists(path))throw new IOException("软件包未安装或组件缺失："+c.PackageFamily);return path;
    }
    public static async Task<List<Candidate>> Running()
    {
        var paths=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var p in Process.GetProcesses())using(p)try{var path=p.MainModule?.FileName;if(path!=null&&path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))paths.TryAdd(path,p.ProcessName);}catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException){}
        var list=new List<Candidate>();
        foreach(var item in paths.OrderBy(x=>x.Value))try{list.Add(new(item.Value,await Identify(item.Key)));}catch(IOException){}
        return list;
    }
    public static async Task<List<Candidate>> StartMenu()
    {
        const string script="""
            $r=@();$shell=New-Object -ComObject WScript.Shell
            foreach($dir in @([Environment]::GetFolderPath('StartMenu'),[Environment]::GetFolderPath('CommonStartMenu'))){
              Get-ChildItem -LiteralPath $dir -Filter *.lnk -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
                $link=$shell.CreateShortcut($_.FullName);if($link.TargetPath -like '*.exe' -and (Test-Path -LiteralPath $link.TargetPath)){$r += [pscustomobject]@{Name=$_.BaseName;Path=$link.TargetPath;PackageFamily='';PackageRelativePath='';ApplicationId=''}}
              }
            }
            Get-AppxPackage | ForEach-Object {
              $p=$_;try{[xml]$m=Get-Content -LiteralPath (Join-Path $p.InstallLocation 'AppxManifest.xml') -Raw
                foreach($a in $m.Package.Applications.Application){if($a.Executable -like '*.exe'){
                  $x=Join-Path $p.InstallLocation $a.Executable;if(Test-Path -LiteralPath $x){$r += [pscustomobject]@{Name=$p.Name;Path=$x;PackageFamily=$p.PackageFamilyName;PackageRelativePath=$a.Executable;ApplicationId=$a.Id}}
                }}
              }catch{}
            }
            ConvertTo-Json -InputObject @($r) -Compress -Depth 4
            """;
        var entries=Parse<StartItem>(await Query(script));
        return entries.DistinctBy(x=>x.Path,StringComparer.OrdinalIgnoreCase).OrderBy(x=>x.Name).Select(x=>new Candidate(x.Name,new(){Path=x.Path,PackageFamily=x.PackageFamily,PackageRelativePath=x.PackageRelativePath,ApplicationId=x.ApplicationId})).ToList();
    }
    sealed record StartItem(string Name,string Path,string PackageFamily,string PackageRelativePath,string ApplicationId);
    [ComImport,Guid("2e941141-7f97-4756-ba1d-9decde894a3d"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivation {[PreserveSig]int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)]string app,[MarshalAs(UnmanagedType.LPWStr)]string? args,int options,out uint pid);}
    public static async Task Launch(AppComponent c)
    {
        string path=await Resolve(c);
        if(c.PackageFamily.Length!=0){
            if(c.ApplicationId.Length==0)throw new IOException("此组件没有独立启动入口，请使用软件主程序。");
            object manager=Activator.CreateInstance(Type.GetTypeFromCLSID(new("45BA127D-10A8-46EA-8AB7-56EA9078943C"))!)!;
            try{int hr=((IActivation)manager).ActivateApplication(c.PackageFamily+"!"+c.ApplicationId,null,0,out _);Marshal.ThrowExceptionForHR(hr);}finally{Marshal.ReleaseComObject(manager);}
        }else Process.Start(new ProcessStartInfo(path){UseShellExecute=true,WorkingDirectory=System.IO.Path.GetDirectoryName(path)});
    }
}
