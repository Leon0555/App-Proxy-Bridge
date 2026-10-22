using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using AppProxyBridge.Core;

int passed=0,failed=0;
void Assert(bool value,string message="Assertion failed"){if(!value)throw new Exception(message);}
async Task Test(string name,Func<Task> body){try{await body().WaitAsync(TimeSpan.FromSeconds(20));Console.WriteLine("PASS "+name);passed++;}catch(Exception e){Console.WriteLine("FAIL "+name+": "+e.Message);failed++;}}
ProxyProfile Profile(Mock m,bool auth=false)=>new(){Name="test",Host=m.Host,Port=m.Port,Protocol=m.Protocol,Authentication=auth,Username=auth?"user":""};
async Task<string> Echo(Stream s,string text){var b=Encoding.UTF8.GetBytes(text);await s.WriteAsync(b);var r=new byte[b.Length+1];await s.ReadExactlyAsync(r);return Encoding.UTF8.GetString(r);}
foreach(var protocol in new[]{ProxyProtocol.HTTP,ProxyProtocol.SOCKS5})foreach(bool auth in new[]{false,true})
    await Test($"{protocol} tunnel auth={auth}",async()=>{using var mock=new Mock(protocol,auth,"A");using var s=await Upstream.Connect(new(Profile(mock,auth),"pass",1),"example.com",443,default);Assert(await Echo(s,"hello")=="Ahello");});
foreach(var protocol in new[]{ProxyProtocol.HTTP,ProxyProtocol.SOCKS5})
    await Test($"{protocol} rejects bad password",async()=>{using var mock=new Mock(protocol,true,"A");try{using var s=await Upstream.Connect(new(Profile(mock,true),"bad",1),"example.com",443,default);throw new Exception("Bad credentials accepted");}catch(IOException){}});
await Test("HTTPS untrusted certificate rejected",async()=>{using var mock=new Mock(ProxyProtocol.HTTPS,false,"A");try{using var s=await Upstream.Connect(new(Profile(mock),"",1),"example.com",443,default);throw new Exception("Invalid cert accepted");}catch(System.Security.Authentication.AuthenticationException){}});
await Test("IPv6 upstream and destination",async()=>{using var mock=new Mock(ProxyProtocol.SOCKS5,false,"V",true);using var s=await Upstream.Connect(new(Profile(mock),"",1),"::1",443,default);Assert(await Echo(s,"six")=="Vsix");Assert(mock.LastTarget=="::1");});
await Test("Gateway authentication and immutable upstreams",async()=>{
    using var a=new Mock(ProxyProtocol.HTTP,false,"A");using var b=new Mock(ProxyProtocol.SOCKS5,false,"B");
    await using var ga=new SnapshotGateway(new(Profile(a),"",1));await using var gb=new SnapshotGateway(new(Profile(b),"",2));ga.Start();gb.Start();
    ProxySnapshot Local(SnapshotGateway g)=>new(new(){Protocol=ProxyProtocol.SOCKS5,Host="127.0.0.1",Port=g.Port,Authentication=true,Username="bridge"},g.Token,0);
    using var old=await Upstream.Connect(Local(ga),"example.com",443,default);Assert(await Echo(old,"old")=="Aold");
    using var fresh=await Upstream.Connect(Local(gb),"example.com",443,default);Assert(await Echo(fresh,"new")=="Bnew");
    Assert(await Echo(old,"still")=="Astill");
    try{using var s=await Upstream.Connect(Local(ga) with{Password="bad"},"example.com",443,default);throw new Exception("Unauthenticated gateway accepted");}catch(IOException){}
});
await Test("Gateway broken upstream never falls back",async()=>{
    var dead=new TcpListener(IPAddress.Loopback,0);dead.Start();int port=((IPEndPoint)dead.LocalEndpoint).Port;dead.Stop();
    await using var gateway=new SnapshotGateway(new(new(){Port=port},"",1));gateway.Start();
    try{using var s=await Upstream.Connect(new(new(){Protocol=ProxyProtocol.SOCKS5,Port=gateway.Port,Authentication=true,Username="bridge"},gateway.Token,0),"example.com",443,default);throw new Exception("Unexpected direct fallback");}catch(IOException){}
});
await Test("SOCKS5 UDP actual relay round trip",async()=>{
    await using var mock=new UdpProxy();
    var p=new ProxyProfile{Protocol=ProxyProtocol.SOCKS5,Port=mock.Port};
    using var assoc=await SocksUdpAssociation.Open(new(p,"",1),default);
    byte[] payload=[0,0,0,1,8,8,8,8,0,53,10,20,30];await assoc.Send(payload,default);
    using var timeout=new CancellationTokenSource(3000);Assert((await assoc.Receive(timeout.Token)).SequenceEqual(payload));
    await using var gateway=new SnapshotGateway(new(p,"",2));gateway.Start();
    using var local=await SocksUdpAssociation.Open(new(new(){Protocol=ProxyProtocol.SOCKS5,Port=gateway.Port,Authentication=true,Username="bridge"},gateway.Token,2),timeout.Token);
    await local.Send(payload,timeout.Token);Assert((await local.Receive(timeout.Token)).SequenceEqual(payload));
});
await Test("HTTP gateway rejects UDP",async()=>{
    await using var gateway=new SnapshotGateway(new(new(){Protocol=ProxyProtocol.HTTP},"",1));gateway.Start();
    try{using var assoc=await SocksUdpAssociation.Open(new(new(){Protocol=ProxyProtocol.SOCKS5,Port=gateway.Port,Authentication=true,Username="bridge"},gateway.Token,0),default);throw new Exception("UDP silently accepted");}catch(IOException){}
});
await Test("DPAPI and JSON have no plaintext password",()=>{
    string dir=Path.Combine(Path.GetTempPath(),"AppProxyBridge-test-"+Guid.NewGuid());var store=new ConfigurationStore(dir);
    var p=new ProxyProfile{Authentication=true,Username="user"};p=p with{CredentialReference=store.SavePassword(p.Id,"secret-marker")};
    store.Save(new(){Profiles=[p]});Assert(store.ReadPassword(p)=="secret-marker");Assert(!File.ReadAllText(Path.Combine(dir,"profiles.json")).Contains("secret-marker"));
    Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(dir,p.CredentialReference))).Contains("secret-marker"));Assert(store.Load().Version==2);return Task.CompletedTask;
});
await Test("Legacy import re-encrypts without changing originals",()=>{
    string root=Path.Combine(Path.GetTempPath(),"APB-import-"+Guid.NewGuid());string old=Path.Combine(root,"old"),fresh=Path.Combine(root,"new");
    var original=new ConfigurationStore(old);var destination=new ConfigurationStore(fresh);
    var p=new ProxyProfile{Authentication=true,Username="user",Name="legacy"};p=p with{CredentialReference=original.SavePassword(p.Id,"test-secret")};
    string source=Path.Combine(old,"profiles.json");File.WriteAllText(source,System.Text.Json.JsonSerializer.Serialize(new Configuration{Version=1,Profiles=[p]}));
    string before=File.ReadAllText(source);var imported=ConfigurationTransfer.ImportLegacy(source,destination);
    Assert(imported.MissingSecrets==0&&imported.Profiles.Count==1);Assert(imported.Profiles[0].Id!=p.Id);Assert(destination.ReadPassword(imported.Profiles[0])=="test-secret");
    Assert(File.ReadAllText(source)==before&&original.ReadPassword(p)=="test-secret");Assert(!ConfigurationTransfer.Export(new(){Profiles=imported.Profiles.ToList()}).Contains("test-secret"));
    return Task.CompletedTask;
});
await Test("Legacy missing password is explicit",()=>{
    string root=Path.Combine(Path.GetTempPath(),"APB-missing-"+Guid.NewGuid());Directory.CreateDirectory(root);string source=Path.Combine(root,"profiles.json");
    var p=new ProxyProfile{Authentication=true,Username="user"};
    File.WriteAllText(source,System.Text.Json.JsonSerializer.Serialize(new Configuration{Version=1,Profiles=[p]}));
    var result=ConfigurationTransfer.ImportLegacy(source,new(Path.Combine(root,"new")));
    Assert(result.MissingSecrets==1&&result.Profiles[0].CredentialReference=="");return Task.CompletedTask;
});
await Test("Stopped gateway releases listener",async()=>{
    var gateway=new SnapshotGateway(new(new(),"",1));gateway.Start();int port=gateway.Port;await gateway.DisposeAsync();
    var listener=new TcpListener(IPAddress.Loopback,port);listener.Start();listener.Stop();
});
await Test("Pipe versioned framing and oversize rejection",async()=>{
    using var stream=new MemoryStream();await PipeProtocol.Write(stream,new ServiceRequest(1,"status"),default);stream.Position=0;
    Assert((await PipeProtocol.Read<ServiceRequest>(stream,default)).Command=="status");
    byte[] head=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(head,PipeProtocol.Limit+1);using var bad=new MemoryStream(head);
    try{await PipeProtocol.Read<ServiceRequest>(bad,default);throw new Exception("Oversize accepted");}catch(IOException){}
});
string? native=Environment.GetEnvironmentVariable("APB_NATIVE");
if(native!=null) {
    NativeLibrary.SetDllImportResolver(typeof(Checks).Assembly,(_,_,_)=>NativeLibrary.Load(Path.GetFullPath(native)));
    await Test("Native full path matching including Unicode and delimiters",()=>{
        Checks.Reset();uint id=Checks.Add(1,"127.0.0.1",1234,"","",false);Assert(id>0);
        var paths=new[]{@"C:\软件 空格\a,b;app.exe"};Assert(Checks.Replace(paths,id,false));
        Assert(Checks.Match(paths[0],"8.8.8.8",false,false)==0);
        Assert(Checks.Match(@"D:\other\a,b;app.exe","8.8.8.8",false,false)==1);
        Assert(Checks.Match("a,b;app.exe","8.8.8.8",false,false)==1);
        Assert(Checks.Match(@"c:\软件 空格\A,B;APP.EXE","2606:4700::1111",false,true)==0);return Task.CompletedTask;
    });
    await Test("Native UDP block, atomic replacement, failed update retention",()=>{
        Assert(Checks.Replace([@"C:\one\app.exe"],1,true));
        Assert(Checks.Match(@"C:\one\app.exe","8.8.8.8",true,false)==2);
        Assert(Checks.Match(@"C:\one\app.exe","2606:4700::1111",true,true)==2);
        Assert(!Checks.Replace(["*.exe"],1,false));Assert(Checks.Match(@"C:\one\app.exe","8.8.8.8",true,false)==2);
        for(int i=0;i<100;i++){Assert(Checks.Replace([@"D:\two\app.exe"],1,false));Assert(Checks.Match(@"C:\one\app.exe","8.8.8.8",false,false)==1);Assert(Checks.Replace([@"C:\one\app.exe"],1,true));}
        Checks.Reset();Assert(Checks.Match(@"C:\one\app.exe","8.8.8.8",false,false)==1);return Task.CompletedTask;
    });
    if(!File.Exists(Path.Combine(Path.GetDirectoryName(native)!,"WinDivert.dll")))await Test("Native missing driver fails and releases listener",()=>{
        Checks.Reset();Assert(!Checks.Start(),"Started without driver");
        var listener=new TcpListener(IPAddress.Any,34910);listener.Start();listener.Stop();
        Checks.Reset();return Task.CompletedTask;
    });
} else Console.WriteLine("SKIP native: set APB_NATIVE to the built native DLL.");
Console.WriteLine($"{passed} passed, {failed} failed");return failed==0?0:1;

static class Checks
{
    const string L="APBTestNative";
    [DllImport(L,EntryPoint="APB_Reset",CallingConvention=CallingConvention.Cdecl)]public static extern void Reset();
    [DllImport(L,EntryPoint="ProxyBridge_Start",CallingConvention=CallingConvention.Cdecl)][return:MarshalAs(UnmanagedType.Bool)]public static extern bool Start();
    [DllImport(L,EntryPoint="ProxyBridge_AddProxyConfig",CallingConvention=CallingConvention.Cdecl)]public static extern uint Add(int type,[MarshalAs(UnmanagedType.LPUTF8Str)]string host,ushort port,[MarshalAs(UnmanagedType.LPUTF8Str)]string user,[MarshalAs(UnmanagedType.LPUTF8Str)]string password,[MarshalAs(UnmanagedType.Bool)]bool domains);
    [DllImport(L,EntryPoint="APB_TestMatch",CallingConvention=CallingConvention.Cdecl)]public static extern int Match([MarshalAs(UnmanagedType.LPUTF8Str)]string path,[MarshalAs(UnmanagedType.LPUTF8Str)]string host,[MarshalAs(UnmanagedType.Bool)]bool udp,[MarshalAs(UnmanagedType.Bool)]bool ipv6);
    [DllImport(L,EntryPoint="APB_ReplaceRules",CallingConvention=CallingConvention.Cdecl)]static extern bool NativeReplace(IntPtr paths,uint count,uint config,[MarshalAs(UnmanagedType.Bool)]bool block);
    public static bool Replace(string[] paths,uint config,bool block){var p=paths.Select(Marshal.StringToCoTaskMemUTF8).ToArray();var a=Marshal.AllocHGlobal(IntPtr.Size*p.Length);try{Marshal.Copy(p,0,a,p.Length);return NativeReplace(a,(uint)p.Length,config,block);}finally{Marshal.FreeHGlobal(a);foreach(var x in p)Marshal.FreeCoTaskMem(x);}}
}
sealed class UdpProxy : IAsyncDisposable
{
    readonly TcpListener tcp=new(IPAddress.Loopback,0);readonly UdpClient udp=new(new IPEndPoint(IPAddress.Loopback,0));readonly CancellationTokenSource stop=new();readonly Task loop,datagrams;
    public int Port=>((IPEndPoint)tcp.LocalEndpoint).Port;
    public UdpProxy(){tcp.Start();loop=Loop();datagrams=Datagrams();}
    async Task Loop(){try{while(true){var c=await tcp.AcceptTcpClientAsync(stop.Token);_=Handle(c);}}catch(OperationCanceledException){}}
    async Task Handle(TcpClient c){using(c)try{var s=c.GetStream();var b=new byte[2];await s.ReadExactlyAsync(b,stop.Token);var m=new byte[b[1]];await s.ReadExactlyAsync(m,stop.Token);await s.WriteAsync(new byte[]{5,0},stop.Token);var h=new byte[4];await s.ReadExactlyAsync(h,stop.Token);await SocksUdpAssociation.ReadAddress(s,h[3],stop.Token);int port=((IPEndPoint)udp.Client.LocalEndPoint!).Port;await s.WriteAsync(new byte[]{5,0,0,1,127,0,0,1,(byte)(port>>8),(byte)port},stop.Token);await s.ReadExactlyAsync(new byte[1],stop.Token);}catch(Exception ex) when(ex is OperationCanceledException or IOException){}}
    async Task Datagrams(){try{while(true){var r=await udp.ReceiveAsync(stop.Token);await udp.SendAsync(r.Buffer,r.RemoteEndPoint,stop.Token);}}catch(OperationCanceledException){}}
    public async ValueTask DisposeAsync(){await stop.CancelAsync();tcp.Stop();udp.Dispose();await Task.WhenAll(loop,datagrams);stop.Dispose();}
}
