using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AppProxyBridge.Core;

public sealed class SocksUdpAssociation : IDisposable
{
    readonly TcpClient control;
    readonly UdpClient udp;
    readonly IPEndPoint relay;
    SocksUdpAssociation(TcpClient control, UdpClient udp, IPEndPoint relay) { this.control=control;this.udp=udp;this.relay=relay; }
    public static async Task<SocksUdpAssociation> Open(ProxySnapshot snapshot, CancellationToken ct)
    {
        if(snapshot.Profile.Protocol!=ProxyProtocol.SOCKS5) throw new IOException("上游不支持 UDP。");
        var tcp=new TcpClient();
        UdpClient? udp=null;
        try {
            await tcp.ConnectAsync(snapshot.Profile.Host,snapshot.Profile.Port,ct);
            var stream=tcp.GetStream();
            await stream.WriteAsync(snapshot.Profile.Authentication?new byte[]{5,1,2}:new byte[]{5,1,0},ct);
            var reply=new byte[2];await stream.ReadExactlyAsync(reply,ct);
            if(reply[0]!=5||reply[1]!=(snapshot.Profile.Authentication?2:0))throw new IOException("SOCKS5 认证方式不匹配。");
            if(snapshot.Profile.Authentication) {
                var user=Encoding.UTF8.GetBytes(snapshot.Profile.Username);var password=Encoding.UTF8.GetBytes(snapshot.Password);
                if(user.Length is <1 or >255 || password.Length is <1 or >255)throw new IOException("账号密码长度无效。");
                await stream.WriteAsync(new byte[]{1,(byte)user.Length}.Concat(user).Concat([(byte)password.Length]).Concat(password).ToArray(),ct);
                await stream.ReadExactlyAsync(reply,ct);if(reply[0]!=1||reply[1]!=0)throw new IOException("SOCKS5 认证失败。");
            }
            await stream.WriteAsync(new byte[]{5,3,0,1,0,0,0,0,0,0},ct);
            var head=new byte[4];await stream.ReadExactlyAsync(head,ct);
            if(head[0]!=5||head[1]!=0||head[2]!=0)throw new IOException("SOCKS5 上游拒绝 UDP ASSOCIATE。");
            var (host,port)=await ReadAddress(stream,head[3],ct);
            var ips=await Dns.GetHostAddressesAsync(host,ct);
            var address=ips.First();
            if(address.Equals(IPAddress.Any)||address.Equals(IPAddress.IPv6Any)) address=((IPEndPoint)tcp.Client.RemoteEndPoint!).Address;
            udp=new UdpClient(address.AddressFamily);udp.Client.Bind(new IPEndPoint(address.AddressFamily==AddressFamily.InterNetwork?IPAddress.Any:IPAddress.IPv6Any,0));
            return new(tcp,udp,new(address,port));
        } catch {udp?.Dispose();tcp.Dispose();throw;}
    }
    public static async Task<(string Host,int Port)> ReadAddress(Stream s,int type,CancellationToken ct)
    {
        int length=type switch{1=>4,4=>16,3=>await ReadByte(s,ct),_=>throw new IOException("SOCKS 地址无效。")};
        var bytes=new byte[length];await s.ReadExactlyAsync(bytes,ct);var port=new byte[2];await s.ReadExactlyAsync(port,ct);
        return(type==3?Encoding.ASCII.GetString(bytes):new IPAddress(bytes).ToString(),(port[0]<<8)|port[1]);
    }
    static async Task<int> ReadByte(Stream s,CancellationToken ct){var b=new byte[1];await s.ReadExactlyAsync(b,ct);return b[0];}
    public async Task Send(byte[] packet,CancellationToken ct)
    {
        if(packet.Length<4||packet[0]!=0||packet[1]!=0||packet[2]!=0)throw new IOException("不支持分片 SOCKS UDP 数据。");
        if(control.Client.Poll(0,SelectMode.SelectRead)&&control.Client.Available==0)throw new IOException("UDP 控制连接已关闭。");
        await udp.SendAsync(packet,relay,ct);
    }
    public async Task<byte[]> Receive(CancellationToken ct)
    {
        while(true){var r=await udp.ReceiveAsync(ct);if(r.RemoteEndPoint.Equals(relay))return r.Buffer;}
    }
    public void Dispose(){udp.Dispose();control.Dispose();}
}
