using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace AppProxyBridge.Core;
public sealed record ServiceRequest(int Version, string Command, Activation? Activation = null, List<string>? Paths = null, int AppCount = 0);
public sealed record ServiceResponse(bool Success, string Message, EngineStatus? Status = null);
public static class PipeProtocol
{
    public const string Name="AppProxyBridge.Control.v1";
    public const int Version=1;
    public const int Limit=1024*1024;
    public static async Task Write<T>(Stream stream,T value,CancellationToken ct)
    {
        byte[] data=JsonSerializer.SerializeToUtf8Bytes(value);
        if(data.Length>Limit)throw new IOException("消息超过大小限制。");
        byte[] head=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(head,data.Length);
        try{await stream.WriteAsync(head,ct);await stream.WriteAsync(data,ct);await stream.FlushAsync(ct);}
        finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(data);}
    }
    public static async Task<T> Read<T>(Stream stream,CancellationToken ct)
    {
        byte[] head=new byte[4];await stream.ReadExactlyAsync(head,ct);
        int size=BinaryPrimitives.ReadInt32LittleEndian(head);
        if(size is <2 or >Limit)throw new IOException("服务消息长度无效。");
        var data=new byte[size];await stream.ReadExactlyAsync(data,ct);
        try{return JsonSerializer.Deserialize<T>(data)??throw new IOException("服务消息为空。");}
        finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(data);}
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageNameW(IntPtr process,int flags,System.Text.StringBuilder path,ref int size);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    static string ServerPath(uint pid)
    {
        var h=OpenProcess(0x1000,false,pid);if(h==IntPtr.Zero)throw new IOException("无法验证服务进程。");
        try{var b=new System.Text.StringBuilder(32768);int length=b.Capacity;if(!QueryFullProcessImageNameW(h,0,b,ref length))throw new IOException("无法读取服务身份。");return b.ToString();}
        finally{CloseHandle(h);}
    }
    public static async Task<ServiceResponse> Call(string command,Activation? activation=null,CancellationToken ct=default,string? servicePath=null,List<string>? paths=null,int appCount=0)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(90));
        using var pipe=new NamedPipeClientStream(".",Name,PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Identification);
        try{await pipe.ConnectAsync(1500,timeout.Token);}catch(TimeoutException){throw new IOException("后台服务未连接。请安装 App Proxy Bridge，或检查服务是否运行。");}
        if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out uint pid))throw new IOException("无法验证后台服务身份。");
        {
            string expected=Path.GetFullPath(servicePath??Path.Combine(AppContext.BaseDirectory,"AppProxyBridge.Service.exe"));
            if(!string.Equals(ServerPath(pid),expected,StringComparison.OrdinalIgnoreCase))throw new IOException("后台服务路径不匹配，未发送配置。");
        }
        await Write(pipe,new ServiceRequest(Version,command,activation,paths,appCount),timeout.Token);
        var result=await Read<ServiceResponse>(pipe,timeout.Token);
        if(!result.Success)throw new IOException(result.Message);
        return result;
    }
}
