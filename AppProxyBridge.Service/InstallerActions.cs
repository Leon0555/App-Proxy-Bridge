using System.Diagnostics;

namespace AppProxyBridge.Service;
static class InstallerActions
{
    const string Rule="App Proxy Bridge relay";
    static void Run(string file,params string[] args)
    {
        var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,file)){UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in args)info.ArgumentList.Add(arg);
        using var p=Process.Start(info)??throw new IOException("无法执行系统配置。");
        if(!p.WaitForExit(30000)){p.Kill();throw new IOException("系统配置超时。");}
        if(p.ExitCode!=0)throw new IOException("系统配置失败："+file);
    }
    public static void Configure()
    {
        Run("netsh.exe","advfirewall","firewall","add","rule","name="+Rule,"dir=in","action=allow","program="+Environment.ProcessPath,"enable=yes","profile=any","protocol=TCP","localport=34910");
        Run("netsh.exe","advfirewall","firewall","add","rule","name="+Rule,"dir=in","action=allow","program="+Environment.ProcessPath,"enable=yes","profile=any","protocol=UDP","localport=34911");
        Run("sc.exe","failure","AppProxyBridge","reset=","86400","actions=","restart/5000");
    }
    public static void Cleanup()=>Run("netsh.exe","advfirewall","firewall","delete","rule","name="+Rule);
}
