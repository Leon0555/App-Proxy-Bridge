using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AppProxyBridge.Core;

namespace AppProxyBridge.UiTests;
static class PublishedSmoke
{
    static Process Start(string path,params string[] args)
    {
        var info=new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(path)!};
        info.Environment.Remove("DOTNET_ROOT");info.Environment.Remove("DOTNET_ROOT_X64");info.Environment["DOTNET_MULTILEVEL_LOOKUP"]="0";
        foreach(var arg in args)info.ArgumentList.Add(arg);
        return Process.Start(info)??throw new IOException("Published process failed to start.");
    }
    static async Task Window(Process p)
    {
        for(int i=0;i<80;i++){if(p.HasExited)throw new Exception("Published UI exited: "+p.ExitCode);p.Refresh();if(p.MainWindowHandle!=IntPtr.Zero)return;await Task.Delay(100);}
        throw new Exception("Published UI did not show a window.");
    }
    public static async Task<int> Run(string directory)
    {
        directory=Path.GetFullPath(directory);
        string servicePath=Path.Combine(directory,"AppProxyBridge.Service.exe");
        string appPath=Path.Combine(directory,"App Proxy Bridge.exe");
        string data=Path.Combine(directory,"..","..","artifacts","published-smoke");
        Process? service=null,app=null,second=null;
        try {
            try{await PipeProtocol.Call("status",servicePath:servicePath);throw new Exception("A service already owns the pipe; smoke test refused.");}
            catch(IOException){}
            service=Start(servicePath,"--console");
            ServiceResponse? state=null;
            for(int i=0;i<6;i++){if(service.HasExited)throw new Exception("Published service exited: "+service.ExitCode);try{state=await PipeProtocol.Call("status",servicePath:servicePath);break;}catch(IOException){await Task.Delay(200);}}
            if(state?.Status?.Running!=false)throw new Exception("Published service did not enter standby.");
            Console.WriteLine("PASS published self-contained service standby");
            app=Start(appPath,"--data-dir",data);await Window(app);
            Console.WriteLine("PASS published self-contained WPF startup with isolated data");
            app.CloseMainWindow();await Task.Delay(600);if(app.HasExited)throw new Exception("Closing window terminated tray process.");
            Console.WriteLine("PASS published close-to-tray");
            second=Start(appPath,"--data-dir",data);await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if(second.ExitCode!=0)throw new Exception("Second instance failed.");await Window(app);
            Console.WriteLine("PASS published single-instance wake");
            await PipeProtocol.Call("stop",servicePath:servicePath);
            if((await PipeProtocol.Call("status",servicePath:servicePath)).Status?.Running!=false)throw new Exception("Stop did not preserve standby.");
            Console.WriteLine("PASS published service stop command");
            return 0;
        }catch(Exception ex){Console.WriteLine("FAIL published smoke: "+ex);return 1;}
        finally{foreach(var p in new[]{second,app,service})if(p!=null){try{if(!p.HasExited){p.Kill();await p.WaitForExitAsync();}}finally{p.Dispose();}}}
    }
}
