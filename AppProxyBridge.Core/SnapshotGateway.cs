using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace AppProxyBridge.Core;

// One immutable upstream per gateway. Native config IDs refer to gateways, never to mutable credentials.
public sealed class SnapshotGateway : IAsyncDisposable
{
    readonly ProxySnapshot snapshot;
    readonly TcpListener listener=new(IPAddress.Loopback,0);
    readonly CancellationTokenSource lifetime=new();
    readonly ConcurrentDictionary<int,Task> workers=new();
    readonly ConcurrentDictionary<int,TcpClient> clients=new();
    Task? accept;int sequence;
    public string Token {get;}=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public int Port=>((IPEndPoint)listener.LocalEndpoint).Port;
    public event Action<string>? Error;
    public SnapshotGateway(ProxySnapshot snapshot){this.snapshot=snapshot;}
    public void Start(){listener.Start();accept=Accept();}
    async Task Accept()
    {
        try {while(!lifetime.IsCancellationRequested) {
            var client=await listener.AcceptTcpClientAsync(lifetime.Token);int id=Interlocked.Increment(ref sequence);clients[id]=client;
            var task=Handle(client);workers[id]=task;
            _=task.ContinueWith(t=>{workers.TryRemove(id,out _);clients.TryRemove(id,out _);},TaskScheduler.Default);
        }}catch(Exception ex) when(ex is OperationCanceledException or SocketException or ObjectDisposedException){}
    }
    async Task Handle(TcpClient client)
    {
        using(client)try {
            var ct=lifetime.Token;using var handshake=CancellationTokenSource.CreateLinkedTokenSource(ct);handshake.CancelAfter(TimeSpan.FromSeconds(snapshot.Profile.TimeoutSeconds));
            var s=client.GetStream();var b=new byte[2];await s.ReadExactlyAsync(b,handshake.Token);
            if(b[0]!=5||b[1]==0)throw new IOException("无效本地 SOCKS 握手。");
            var methods=new byte[b[1]];await s.ReadExactlyAsync(methods,handshake.Token);
            if(!methods.Contains((byte)2)){await s.WriteAsync(new byte[]{5,255},ct);return;}
            await s.WriteAsync(new byte[]{5,2},ct);
            await s.ReadExactlyAsync(b,handshake.Token);if(b[0]!=1)throw new IOException("认证版本无效。");
            var user=new byte[b[1]];await s.ReadExactlyAsync(user,handshake.Token);await s.ReadExactlyAsync(b.AsMemory(0,1),handshake.Token);
            var pass=new byte[b[0]];await s.ReadExactlyAsync(pass,handshake.Token);
            bool ok=Encoding.UTF8.GetString(user)=="bridge"&&CryptographicOperations.FixedTimeEquals(pass,Encoding.UTF8.GetBytes(Token));
            await s.WriteAsync(new byte[]{1,(byte)(ok?0:1)},ct);if(!ok)return;
            var head=new byte[4];await s.ReadExactlyAsync(head,handshake.Token);if(head[0]!=5||head[2]!=0)throw new IOException("本地 SOCKS 请求无效。");
            var target=await SocksUdpAssociation.ReadAddress(s,head[3],handshake.Token);
            if(head[1]==1){
                bool acknowledged=false;
                try {
                    using var remote=await Upstream.Connect(snapshot,target.Host,target.Port,handshake.Token);
                    await s.WriteAsync(new byte[]{5,0,0,1,127,0,0,1,0,0},ct);
                    acknowledged=true;
                    using var relay=CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var a=s.CopyToAsync(remote,relay.Token);var d=remote.CopyToAsync(s,relay.Token);
                    await Task.WhenAny(a,d);await relay.CancelAsync();try{await Task.WhenAll(a,d);}catch(OperationCanceledException){}
                }catch(Exception ex) when(!ct.IsCancellationRequested){if(!acknowledged)await Reject(s,ct);Error?.Invoke(Upstream.Friendly(ex));}
            } else if(head[1]==3) {
                if(snapshot.Profile.Protocol!=ProxyProtocol.SOCKS5){await Reject(s,ct);return;}
                try {
                    using var association=await SocksUdpAssociation.Open(snapshot,handshake.Token);
                    using var udp=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));int port=((IPEndPoint)udp.Client.LocalEndPoint!).Port;
                    await s.WriteAsync(new byte[]{5,0,0,1,127,0,0,1,(byte)(port>>8),(byte)port},ct);
                    using var relay=CancellationTokenSource.CreateLinkedTokenSource(ct);var rc=relay.Token;
                    IPEndPoint? peer=null;
                    async Task Send(){while(true){var r=await udp.ReceiveAsync(rc);if(!IPAddress.IsLoopback(r.RemoteEndPoint.Address))continue;peer??=r.RemoteEndPoint;if(peer.Equals(r.RemoteEndPoint))await association.Send(r.Buffer,rc);}}
                    async Task Receive(){while(true){var r=await association.Receive(rc);if(peer!=null)await udp.SendAsync(r,peer,rc);}}
                    var send=Send();var receive=Receive();var control=s.ReadAsync(new byte[1],rc).AsTask();
                    await Task.WhenAny(send,receive,control);await relay.CancelAsync();
                    try{await Task.WhenAll(send,receive,control);}catch(OperationCanceledException){}
                }catch(Exception ex) when(!ct.IsCancellationRequested){await Reject(s,ct);Error?.Invoke(Upstream.Friendly(ex));}
            } else await Reject(s,ct);
        }catch(Exception ex) when(ex is not OutOfMemoryException){if(!lifetime.IsCancellationRequested)Error?.Invoke(Upstream.Friendly(ex));}
    }
    static async Task Reject(Stream s,CancellationToken ct){try{await s.WriteAsync(new byte[]{5,1,0,1,0,0,0,0,0,0},ct);}catch(IOException){}}
    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();listener.Stop();foreach(var c in clients.Values)c.Dispose();
        if(accept!=null)await accept;try{await Task.WhenAll(workers.Values).WaitAsync(TimeSpan.FromSeconds(5));}catch(Exception ex) when(ex is OperationCanceledException or TimeoutException or IOException or SocketException){}
        lifetime.Dispose();
    }
}
