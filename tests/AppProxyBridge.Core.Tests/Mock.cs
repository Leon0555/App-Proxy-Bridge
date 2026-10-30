using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AppProxyBridge.Core;
sealed class Mock : IDisposable
{
    readonly TcpListener listener;readonly CancellationTokenSource stop=new();readonly bool auth;readonly string marker;readonly X509Certificate2? cert;
    public ProxyProtocol Protocol{get;}public string Host{get;}public int Port=>((IPEndPoint)listener.LocalEndpoint).Port;public string LastTarget="",LastBody="";
    public Mock(ProxyProtocol protocol,bool auth,string marker,bool ipv6=false){Protocol=protocol;this.auth=auth;this.marker=marker;Host=ipv6?"::1":"127.0.0.1";listener=new(IPAddress.Parse(Host),0);listener.Start();if(protocol==ProxyProtocol.HTTPS){using var rsa=RSA.Create(2048);var req=new CertificateRequest("CN=localhost",rsa,System.Security.Cryptography.HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);using var generated=req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1),DateTimeOffset.Now.AddDays(1));cert=X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx),null,X509KeyStorageFlags.Exportable);}_=Loop();}
    async Task Loop(){try{while(!stop.IsCancellationRequested){var c=await listener.AcceptTcpClientAsync(stop.Token);_=Handle(c);}}catch(OperationCanceledException){}catch(ObjectDisposedException){}}
    async Task Handle(TcpClient c){using(c)try{Stream s=c.GetStream();if(Protocol==ProxyProtocol.HTTPS){var tls=new SslStream(s);s=tls;await tls.AuthenticateAsServerAsync(cert!);}
        if(Protocol==ProxyProtocol.SOCKS5){byte[] hello=new byte[2];await s.ReadExactlyAsync(hello);byte[] methods=new byte[hello[1]];await s.ReadExactlyAsync(methods);await s.WriteAsync(new byte[]{5,(byte)(auth?2:0)});if(auth){byte[] first=new byte[2];await s.ReadExactlyAsync(first);var u=new byte[first[1]];await s.ReadExactlyAsync(u);byte[] len=new byte[1];await s.ReadExactlyAsync(len);var p=new byte[len[0]];await s.ReadExactlyAsync(p);bool ok=Encoding.UTF8.GetString(u)=="user"&&Encoding.UTF8.GetString(p)=="pass";await s.WriteAsync(new byte[]{1,(byte)(ok?0:1)});if(!ok)return;}byte[] head=new byte[4];await s.ReadExactlyAsync(head);int n=head[3]==1?4:head[3]==4?16:await ReadByte(s);byte[] addr=new byte[n];await s.ReadExactlyAsync(addr);LastTarget=head[3]==3?Encoding.ASCII.GetString(addr):new IPAddress(addr).ToString();await s.ReadExactlyAsync(new byte[2]);await s.WriteAsync(new byte[]{5,0,0,1,127,0,0,1,0,80});}
        else {string head=await Wire.Header(s,default);if(auth&&!head.Contains("Proxy-Authorization: Basic dXNlcjpwYXNz",StringComparison.OrdinalIgnoreCase)){await Wire.Write(s,"HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=mock\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",default);return;}
            if(head.StartsWith("CONNECT")){LastTarget=head.Split(' ')[1].Split(':')[0];await Wire.Write(s,"HTTP/1.1 200 Connection Established\r\n\r\n",default);}else{string body="";if(head.Contains("Transfer-Encoding: chunked",StringComparison.OrdinalIgnoreCase)){while(true){string line=await Line(s);int len=Convert.ToInt32(line,16);if(len==0){await Line(s);break;}byte[] b=new byte[len];await s.ReadExactlyAsync(b);body+=Encoding.UTF8.GetString(b);await Line(s);}}else{var match=System.Text.RegularExpressions.Regex.Match(head,@"Content-Length:\s*(\d+)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);if(match.Success){byte[] b=new byte[int.Parse(match.Groups[1].Value)];await s.ReadExactlyAsync(b);body=Encoding.UTF8.GetString(b);}}LastBody=body;await Wire.Write(s,$"HTTP/1.1 200 OK\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}",default);return;}}
        byte[] buffer=new byte[4096];while(true){int n=await s.ReadAsync(buffer,stop.Token);if(n==0)return;await s.WriteAsync(Encoding.ASCII.GetBytes(marker));await s.WriteAsync(buffer.AsMemory(0,n));}
    }catch(Exception){} }
    static async Task<int> ReadByte(Stream s){byte[] b=new byte[1];await s.ReadExactlyAsync(b);return b[0];}
    static async Task<string> Line(Stream s){var b=new List<byte>();while(true){b.Add((byte)await ReadByte(s));if(b.Count>=2&&b[^2]==13&&b[^1]==10)return Encoding.ASCII.GetString(b.ToArray(),0,b.Count-2);}}
    public void Dispose(){stop.Cancel();listener.Stop();cert?.Dispose();stop.Dispose();}
}

