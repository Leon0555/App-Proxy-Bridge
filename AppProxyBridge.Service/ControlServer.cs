using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using AppProxyBridge.Core;

namespace AppProxyBridge.Service;
public sealed class ControlServer(IProxyEngine engine,SecurityIdentifier owner)
{
    readonly SemaphoreSlim commands=new(1);
    public async Task Run(CancellationToken ct)
    {
        var tasks=new List<Task>();
        try {
            while(!ct.IsCancellationRequested) {
                var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);
                security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
                foreach(var sid in new[]{owner,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})
                    security.AddAccessRule(new(sid,PipeAccessRights.FullControl,AccessControlType.Allow));
                var pipe=NamedPipeServerStreamAcl.Create(PipeProtocol.Name,PipeDirection.InOut,8,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,65536,65536,security);
                try{await pipe.WaitForConnectionAsync(ct);}catch{pipe.Dispose();throw;}
                tasks.RemoveAll(t=>t.IsCompleted);tasks.Add(Handle(pipe,ct));
            }
        }catch(OperationCanceledException) when(ct.IsCancellationRequested){}
        finally{await Task.WhenAll(tasks);}
    }
    async Task Handle(NamedPipeServerStream pipe,CancellationToken lifetime)
    {
        using(pipe)using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime)) {
            timeout.CancelAfter(TimeSpan.FromSeconds(90));var ct=timeout.Token;
            try {
                var request=await PipeProtocol.Read<ServiceRequest>(pipe,ct);
                string? sid=null;pipe.RunAsClient(()=>sid=WindowsIdentity.GetCurrent(true)?.User?.Value);
                if(sid!=owner.Value)throw new UnauthorizedAccessException("当前用户未获得此服务的控制权限。");
                if(request.Version!=PipeProtocol.Version)throw new IOException("通信版本不兼容，请重新安装匹配版本。");
                await commands.WaitAsync(ct);
                try {
                    switch(request.Command) {
                        case "status":break;
                        case "apply":await engine.Apply(request.Activation??throw new IOException("缺少配置。"),ct);break;
                        case "stop":await engine.Stop();break;
                        case "rules":await engine.UpdateRules(request.Paths??throw new IOException("缺少软件规则。"),request.AppCount,ct);break;
                        default:throw new IOException("不支持的服务命令。");
                    }
                    await PipeProtocol.Write(pipe,new ServiceResponse(true,"",engine.Status),ct);
                }finally{commands.Release();}
            }catch(Exception ex) when(ex is not OutOfMemoryException) {
                try{await PipeProtocol.Write(pipe,new ServiceResponse(false,ex is UnauthorizedAccessException?"当前用户无服务控制权限。":ex.Message),ct);}catch(Exception e) when(e is IOException or OperationCanceledException){}
            }
        }
    }
}
