using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace AppProxyBridge.Core;

public interface IProxyEngine : IAsyncDisposable
{
    EngineStatus Status {get;}
    Task Apply(Activation activation,CancellationToken ct);
    Task UpdateRules(List<string> paths,int count,CancellationToken ct);
    Task Stop();
}
public sealed class NativeEngine : IProxyEngine
{
    const string Lib="AppProxyBridgeNative.dll";
    readonly ConcurrentQueue<ConnectionEvent> events=new();
    readonly List<SnapshotGateway> gateways=[];
    readonly SemaphoreSlim gate=new(1);
    readonly LogCallback logCallback;
    readonly ConnectionCallback connectionCallback;
    bool loaded,running;
    long revision;string active="",detail="服务待机，请手动启用代理。";int selected;
    List<string> appliedPaths=[];Guid? profileId;uint configId;bool blockedUdp;
    readonly string ownerSid;
    public NativeEngine(string? ownerSid=null){this.ownerSid=ownerSid??System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;logCallback=OnLog;connectionCallback=OnConnection;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void LogCallback(IntPtr text);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void ConnectionCallback(IntPtr name,uint pid,IntPtr host,ushort port,IntPtr action);
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern void ProxyBridge_SetLogCallback(LogCallback callback);
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern void ProxyBridge_SetConnectionCallback(ConnectionCallback callback);
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern void ProxyBridge_SetTrafficLoggingEnabled([MarshalAs(UnmanagedType.Bool)]bool enabled);
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern uint ProxyBridge_AddProxyConfig(int type,[MarshalAs(UnmanagedType.LPUTF8Str)]string host,ushort port,[MarshalAs(UnmanagedType.LPUTF8Str)]string user,[MarshalAs(UnmanagedType.LPUTF8Str)]string password,[MarshalAs(UnmanagedType.Bool)]bool domains);
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.Bool)]static extern bool ProxyBridge_Start();
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.Bool)]static extern bool ProxyBridge_Stop();
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern void APB_Reset();
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.Bool)]static extern bool APB_SetOwnerSid([MarshalAs(UnmanagedType.LPUTF8Str)]string sid);
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.Bool)]static extern bool APB_ReplaceRules(IntPtr paths,uint count,uint config,[MarshalAs(UnmanagedType.Bool)]bool blockUdp);
    [StructLayout(LayoutKind.Sequential)]unsafe struct NativeFlow {
        public ulong Id;public uint Pid,ConfigId,RuleId,Age;public ulong Sent,Received;public ushort Port;public byte Udp,IPv6;public fixed byte Dest[16];public fixed byte Name[40];public fixed byte Host[64];
    }
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern uint ProxyBridge_GetFlows([Out]NativeFlow[] flows,uint max);
    void Record(string process,uint pid,string host,string action)
    {
        events.Enqueue(new(DateTimeOffset.Now,pid,process,host,action));while(events.Count>500)events.TryDequeue(out _);
    }
    void OnLog(IntPtr message){string s=Marshal.PtrToStringUTF8(message)??"";Record("引擎",0,"",s);}
    void OnConnection(IntPtr name,uint pid,IntPtr host,ushort port,IntPtr action)=>Record(Marshal.PtrToStringUTF8(name)??"",pid,(Marshal.PtrToStringUTF8(host)??"")+":"+port,Marshal.PtrToStringUTF8(action)??"");
    public EngineStatus Status
    {
        get {
            gate.Wait();
            try{return new(running,active,revision,selected,detail,loaded?GetFlows():[],events.ToArray(),profileId,appliedPaths.ToArray());}
            finally{gate.Release();}
        }
    }
    unsafe List<Flow> GetFlows()
    {
        if(!running)return [];
        var native=new NativeFlow[400];uint count=ProxyBridge_GetFlows(native,400);var result=new List<Flow>();
        for(int i=0;i<count;i++){
            var f=native[i];string name=Encoding.UTF8.GetString(new ReadOnlySpan<byte>(f.Name,40)).TrimEnd('\0');
            string host=Encoding.UTF8.GetString(new ReadOnlySpan<byte>(f.Host,64)).TrimEnd('\0');
            result.Add(new(f.Id,f.Pid,f.ConfigId,name,host,f.Port,f.Udp!=0,f.IPv6!=0,f.Sent,f.Received));
        }
        return result;
    }
    void EnsureLoaded()
    {
        if(loaded)return;
        foreach(string file in new[]{Lib,"WinDivert.dll","WinDivert64.sys"})
            if(!File.Exists(Path.Combine(AppContext.BaseDirectory,file)))throw new IOException($"缺少 {file}。驱动可能被安全软件阻止；尚未启用代理。");
        NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory,Lib));
        if(!APB_SetOwnerSid(ownerSid))throw new IOException("无法设置安装用户的进程隔离。");
        ProxyBridge_SetLogCallback(logCallback);ProxyBridge_SetConnectionCallback(connectionCallback);ProxyBridge_SetTrafficLoggingEnabled(true);loaded=true;
    }
    public async Task Apply(Activation activation,CancellationToken ct)
    {
        activation=activation.Validate();
        await gate.WaitAsync(ct);
        try{EnsureLoaded();}finally{gate.Release();}
        var snapshot=new ProxySnapshot(activation.Profile,activation.Password,revision+1);
        var probe=await Upstream.Probe(snapshot,ct);
        if(!probe.Success)throw new IOException("测试失败，当前配置保持不变："+string.Join("；",probe.Steps.Where(s=>!s.Success).Select(s=>s.Detail)));
        await gate.WaitAsync(ct);
        SnapshotGateway? candidate=null;
        try {
            EnsureLoaded();
            if(gateways.Count>=1024)throw new IOException("本次运行的配置版本达到上限，请停止后重新启用。当前配置保持不变。");
            candidate=new(snapshot);candidate.Error+=s=>Record("网关",0,"",s);candidate.Start();
            uint id=ProxyBridge_AddProxyConfig(1,"127.0.0.1",(ushort)candidate.Port,"bridge",candidate.Token,false);
            if(id==0)throw new IOException("无法建立新代理版本。");
            bool blockUdp=activation.Profile.Protocol!=ProxyProtocol.SOCKS5||probe.Steps.Any(s=>s.Name=="UDP 协商"&&!s.Success);
            if(!Replace(activation.Paths,id,blockUdp))throw new IOException("无法应用软件规则。当前配置保持不变。");
            if(!running&&!ProxyBridge_Start()){
                APB_ReplaceRules(IntPtr.Zero,0,0,false);APB_Reset();
                throw new IOException("驱动或本地转发监听启动失败，尚未启用代理。请查看连接记录。");
            }
            running=true;gateways.Add(candidate);candidate=null;revision++;active=activation.Profile.Name;selected=activation.ApplicationCount;
            appliedPaths=activation.Paths;profileId=activation.Profile.Id;configId=id;blockedUdp=blockUdp;
            detail=blockUdp?"已启用 TCP；选中软件的 UDP 被阻止。":"已启用 TCP/UDP；已有连接保留原路径或代理版本。";
        } finally {if(candidate!=null)await candidate.DisposeAsync();gate.Release();}
    }
    public async Task UpdateRules(List<string> paths,int count,CancellationToken ct)
    {
        if(count<0||count>paths.Count||(count==0&&paths.Count!=0))throw new IOException("软件数量无效。");
        if(paths.Count>0)new Activation(new(),"",paths,count).Validate();
        await gate.WaitAsync(ct);
        try{if(!running)throw new IOException("请先启用代理。");if(!Replace(paths,configId,blockedUdp))throw new IOException("软件规则更新失败，保留原规则。");appliedPaths=paths.ToList();selected=count;}
        finally{gate.Release();}
    }
    static bool Replace(List<string> paths,uint config,bool blockUdp)
    {
        var handles=paths.Select(Marshal.StringToCoTaskMemUTF8).ToArray();var list=Marshal.AllocHGlobal(IntPtr.Size*handles.Length);
        try{Marshal.Copy(handles,0,list,handles.Length);return APB_ReplaceRules(list,(uint)handles.Length,config,blockUdp);}
        finally{Marshal.FreeHGlobal(list);foreach(var p in handles)Marshal.FreeCoTaskMem(p);}
    }
    public async Task Stop()
    {
        await gate.WaitAsync();
        try {
            foreach(var gateway in gateways)await gateway.DisposeAsync();gateways.Clear();
            if(loaded){if(running)ProxyBridge_Stop();APB_Reset();}
            running=false;active="";selected=0;profileId=null;appliedPaths=[];detail="代理已停止，后续连接恢复系统原有路径。";
        }finally{gate.Release();}
    }
    public async ValueTask DisposeAsync(){await Stop();gate.Dispose();GC.KeepAlive(logCallback);GC.KeepAlive(connectionCallback);}
}
