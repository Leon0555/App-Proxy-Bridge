using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AppProxyBridge.App;
using AppProxyBridge.Core;
using AppProxyBridge.Service;

namespace AppProxyBridge.UiTests;
static class Program
{
    [STAThread]static int Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--published")return PublishedSmoke.Run(args[1]).GetAwaiter().GetResult();
        int pass=0;
        void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);pass++;}
        string root=args.FirstOrDefault()??Path.Combine(Path.GetTempPath(),"AppProxyBridge-ui-"+Guid.NewGuid());Directory.CreateDirectory(root);
        // Render without claiming the desktop mutex or waking the user's running instance.
        var app=new AppProxyBridge.App.App(startDesktop:false);app.InitializeComponent();
        var store=new ConfigurationStore(root);
        store.Save(new(){Profiles=[new(){Name="公司代理",Host="127.0.0.1",Port=7890}],Applications=[new(){Name="示例编辑器",Components=[new(){Path=@"C:\示例软件\Editor.exe"}]},new(){Name="示例浏览器",Enabled=false,Components=[new(){Path=@"C:\示例软件\Browser.exe"}]}]});
        var vm=new MainViewModel(root);var window=new MainWindow(vm);
        if(args.Contains("--minimum-window")){window.Width=window.MinWidth;window.Height=window.MinHeight;}
        window.Show();window.UpdateLayout();
        Check(vm.Applications.Count==2,"software list loads independently of profiles");
        vm.Applications[1].Enabled=true;
        Check(vm.Pending && store.Load().Applications[1].Enabled,"checkbox saved and marked pending");
        vm.Page="代理配置";Check(vm.ProfileVisible&&!vm.SoftwareVisible,"page navigation");
        vm.ProfileName="测试配置";vm.Host="localhost";vm.Port="1080";
        vm.SaveCommand.Execute(null);
        Check(!vm.EditorDirty&&store.Load().Profiles[0].Host=="localhost","profile edit saves");
        Check(vm.Applications.All(x=>x.Enabled),"profile save preserves selected applications");
        vm.DuplicateCommand.Execute(null);Check(vm.Password==""&&vm.ProfileName.EndsWith("副本"),"profile duplicate");
        vm.SaveCommand.Execute(null);Check(store.Load().Profiles.Count==2,"duplicate saved separately");
        vm.DefaultCommand.Execute(null);Check(store.Load().Settings.DefaultProfileId==vm.Selected!.Id,"default profile persisted");
        foreach(string theme in new[]{"Dark","Light"}) {
            vm.Theme=theme;Check(store.Load().Settings.Theme==theme,"theme persisted "+theme);
            foreach(string page in new[]{"软件","代理配置","连接记录","设置"}){
                vm.Page=page;window.UpdateLayout();Pump();
                var content=(FrameworkElement)window.Content;var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
                string output=Path.Combine(root,theme+"-"+page+".png");using var file=File.Create(output);png.Save(file);
                Check(new FileInfo(output).Length>5000,"render "+theme+" "+page);
                if(page=="代理配置"){
                    var scroll=(ScrollViewer)window.FindName("ProfileScroll");
                    Console.WriteLine($"Profile layout: window {window.ActualWidth}x{window.ActualHeight}, viewport {scroll.ViewportHeight:F0}, content {scroll.ExtentHeight:F0}, overflow {scroll.ScrollableHeight:F0}");
                }
            }
        }
        Check(VisualTreeHelper.GetChildrenCount(window)>0,"WPF visual tree created");
        window.Hide();
        if(args.Contains("--installed")){
            var catalog=Discovery.StartMenu().GetAwaiter().GetResult();
            Check(catalog.Count>0,"installed start-menu catalog discovery without launch");
            var packaged=catalog.FirstOrDefault(c=>c.Component.PackageFamily.StartsWith("OpenAI.Codex",StringComparison.OrdinalIgnoreCase))??catalog.FirstOrDefault(c=>c.Component.PackageFamily.Length>0);
            if(packaged!=null){
                string path=Discovery.Resolve(packaged.Component).GetAwaiter().GetResult();
                Check(File.Exists(path),"installed package path resolved from stable identity");
                var identified=Discovery.Identify(path).GetAwaiter().GetResult();
                Check(identified.PackageFamily==packaged.Component.PackageFamily&&identified.PackageRelativePath.Length>0,"installed package identity round trip");
            }
        }
        try{ServiceChecks().GetAwaiter().GetResult();Check(true,"secured pipe authentication, version and dispatch");}
        catch(Exception ex){Console.WriteLine("FAIL pipe "+ex);return 1;}
        Console.WriteLine(pass+" UI/service checks passed; previews: "+root);
        app.Shutdown();return 0;
    }
    static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    static async Task ServiceChecks()
    {
        using var stop=new CancellationTokenSource();await using var engine=new FakeEngine();
        var server=new ControlServer(engine,WindowsIdentity.GetCurrent().User!);var task=server.Run(stop.Token);
        try {
            var response=await PipeProtocol.Call("status",servicePath:Environment.ProcessPath);
            if(response.Status?.Running!=false)throw new Exception("Unexpected initial state");
            try{await PipeProtocol.Call("status",servicePath:Path.Combine(Path.GetTempPath(),"wrong-service.exe"));throw new Exception("Squatter accepted");}catch(IOException){}
            using(var pipe=new NamedPipeClientStream(".",PipeProtocol.Name,PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Identification)){
                await pipe.ConnectAsync(2000);await PipeProtocol.Write(pipe,new ServiceRequest(99,"status"),default);
                if((await PipeProtocol.Read<ServiceResponse>(pipe,default)).Success)throw new Exception("Version accepted");
            }
            using(var pipe=new NamedPipeClientStream(".",PipeProtocol.Name,PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Identification)){
                await pipe.ConnectAsync(2000);await PipeProtocol.Write(pipe,new ServiceRequest(1,"launch-arbitrary-executable"),default);
                if((await PipeProtocol.Read<ServiceResponse>(pipe,default)).Success)throw new Exception("Unknown command accepted");
            }
            await PipeProtocol.Call("stop",servicePath:Environment.ProcessPath);
            if(engine.Stops!=1)throw new Exception("Stop was not dispatched");
        }finally{stop.Cancel();await task;}
    }
    sealed class FakeEngine : IProxyEngine
    {
        public int Stops;
        public EngineStatus Status=>new(false,"",0,0,"test",[],[]);
        public Task Apply(Activation a,CancellationToken c)=>throw new NotSupportedException();
        public Task UpdateRules(List<string> p,int n,CancellationToken c)=>throw new NotSupportedException();
        public Task Stop(){Stops++;return Task.CompletedTask;}
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
