using System.Runtime.InteropServices;
using System.Security.Principal;
using AppProxyBridge.Core;
using Microsoft.Win32;

namespace AppProxyBridge.Service;
static class Program
{
    const string ServiceName="AppProxyBridge";
    static readonly CancellationTokenSource stopping=new();
    static readonly Handler handler=Control;
    static readonly MainCallback main=ServiceMain;
    static IntPtr statusHandle;
    static IProxyEngine? activeEngine;
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Table {public string? Name;public MainCallback? Main;}
    [StructLayout(LayoutKind.Sequential)]struct Status {public int Type,State,Accepted,Win32Exit,ServiceExit,Checkpoint,WaitHint;}
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate void MainCallback(int argc,IntPtr argv);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate int Handler(int control,int eventType,IntPtr eventData,IntPtr context);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool StartServiceCtrlDispatcherW([In]Table[] table);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode)]static extern IntPtr RegisterServiceCtrlHandlerExW(string name,Handler handler,IntPtr context);
    [DllImport("advapi32.dll")]static extern bool SetServiceStatus(IntPtr handle,ref Status status);
    static int Main(string[] args)
    {
        if(args.Contains("--configure")){InstallerActions.Configure();return 0;}
        if(args.Contains("--cleanup")){InstallerActions.Cleanup();return 0;}
        if(args.Contains("--console")) {
            Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stopping.Cancel();};
            Run(WindowsIdentity.GetCurrent().User!).GetAwaiter().GetResult();return 0;
        }
        if(!StartServiceCtrlDispatcherW([new(){Name=ServiceName,Main=main},new()]))return Marshal.GetLastWin32Error();
        return 0;
    }
    static void SetState(int state){var status=new Status{Type=0x10,State=state,Accepted=state==4?0x85:0,WaitHint=30000,Win32Exit=state==1?Environment.ExitCode:0};SetServiceStatus(statusHandle,ref status);}
    static int Control(int command,int type,IntPtr data,IntPtr context)
    {
        if(command is 1 or 5){SetState(3);stopping.Cancel();}
        if(command==14 && type==6 && activeEngine!=null)_=activeEngine.Stop(); // logoff returns to standby
        return 0;
    }
    static void ServiceMain(int count,IntPtr args)
    {
        statusHandle=RegisterServiceCtrlHandlerExW(ServiceName,handler,IntPtr.Zero);SetState(2);
        try {
            var sid=(string?)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\AppProxyBridge","OwnerSid",null);
            if(string.IsNullOrWhiteSpace(sid))throw new IOException("服务缺少安装用户信息。");
            SetState(4);Run(new(sid)).GetAwaiter().GetResult();
        }catch(Exception){Environment.ExitCode=1;}
        finally{SetState(1);}
    }
    static async Task Run(SecurityIdentifier owner)
    {
        await using var engine=new NativeEngine(owner.Value);
        activeEngine=engine;
        await new ControlServer(engine,owner).Run(stopping.Token);
    }
}
