using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AppProxyBridge.Core;
using Microsoft.Win32;

namespace AppProxyBridge.App;
public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    protected bool Set<T>(ref T field,T value,[CallerMemberName]string? name=null){if(Equals(field,value))return false;field=value;Notify(name);return true;}
}
public sealed class ActionCommand(Func<Task> action) : ICommand
{
    public event EventHandler? CanExecuteChanged{add{}remove{}}
    public bool CanExecute(object? parameter)=>true;
    public async void Execute(object? parameter){await action();}
}
public sealed class SoftwareRow : Observable
{
    public ApplicationEntry Entry{get;}
    readonly Action changed;
    public SoftwareRow(ApplicationEntry entry,Action changed){Entry=entry;this.changed=changed;}
    public bool Enabled{get=>Entry.Enabled;set{if(Entry.Enabled==value)return;Entry.Enabled=value;Notify();changed();}}
    public string Name=>Entry.Name;
    public string Paths=>string.Join(Environment.NewLine,Entry.Components.Select(x=>x.Path));
    string state="等待检查";public string State{get=>state;set=>Set(ref state,value);}
    int connections;public int Connections{get=>connections;set=>Set(ref connections,value);}
    public System.Windows.Media.ImageSource? Icon{get;set;}
    public void Changed(){Notify(nameof(Paths));Notify(nameof(Name));}
}
public sealed class MainViewModel : Observable
{
    readonly ConfigurationStore store;
    public Configuration Data{get;}
    public ObservableCollection<ProxyProfile> Profiles{get;}=[];
    public ObservableCollection<SoftwareRow> Applications{get;}=[];
    public ObservableCollection<ConnectionEvent> Events{get;}=[];
    public ObservableCollection<Flow> Flows{get;}=[];
    public ObservableCollection<string> Results{get;}=[];
    public string[] Protocols{get;}=["HTTP","HTTPS","SOCKS5"];
    public string[] Themes{get;}=["System","Light","Dark"];
    public string VersionText=>"0.1.0 · Windows x64";
    public string DataDirectory=>store.DirectoryPath;
    string page="软件";public string Page{get=>page;set{if(Set(ref page,value)){Notify(nameof(SoftwareVisible));Notify(nameof(ProfileVisible));Notify(nameof(ConnectionsVisible));Notify(nameof(SettingsVisible));}}}
    public bool SoftwareVisible=>page=="软件";public bool ProfileVisible=>page=="代理配置";public bool ConnectionsVisible=>page=="连接记录";public bool SettingsVisible=>page=="设置";
    bool busy;public bool Busy{get=>busy;private set{Set(ref busy,value);Notify(nameof(Available));}}
    public bool Available=>!Busy;
    string message="添加并勾选软件，然后应用代理。";public string Message{get=>message;set=>Set(ref message,value);}
    string runtime="后台服务未连接";public string Runtime{get=>runtime;set=>Set(ref runtime,value);}
    string activeText="当前配置：无";public string ActiveText{get=>activeText;set=>Set(ref activeText,value);}
    bool pending;public bool Pending{get=>pending;set{Set(ref pending,value);Notify(nameof(PendingText));}}
    public string PendingText=>Pending?"软件选择有待应用的修改":"软件列表已保存";
    ProxyProfile? selected;bool loading;
    public ProxyProfile? Selected{get=>selected;set{if(loading||value==selected)return;if(EditorDirty&&MessageBox.Show("放弃尚未保存的代理配置？","切换配置",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;selected=value;Notify();LoadProfile();}}
    SoftwareRow? software;public SoftwareRow? SelectedSoftware{get=>software;set=>Set(ref software,value);}
    bool editorDirty;public bool EditorDirty{get=>editorDirty;private set{Set(ref editorDirty,value);Notify(nameof(EditorState));}}
    public string EditorState=>EditorDirty?"有未保存的修改":"配置已保存；应用后才会生效";
    void Edit(){if(!loading)EditorDirty=true;}
    string name="",host="127.0.0.1",port="7890",protocol="HTTP",username="",password="",timeout="20",testUrl="https://example.com/";
    bool authentication;
    public string ProfileName{get=>name;set{if(Set(ref name,value))Edit();}}
    public string Host{get=>host;set{if(Set(ref host,value))Edit();}}
    public string Port{get=>port;set{if(Set(ref port,value))Edit();}}
    public string Protocol{get=>protocol;set{if(Set(ref protocol,value))Edit();}}
    public string Username{get=>username;set{if(Set(ref username,value))Edit();}}
    public string Password{get=>password;set{if(Set(ref password,value))Edit();}}
    public bool Authentication{get=>authentication;set{if(Set(ref authentication,value))Edit();}}
    public string Timeout{get=>timeout;set{if(Set(ref timeout,value))Edit();}}
    public string TestUrl{get=>testUrl;set{if(Set(ref testUrl,value))Edit();}}
    public string Theme{get=>Data.Settings.Theme;set{if(Data.Settings.Theme==value)return;Data.Settings.Theme=value;Persist();Notify();((App)Application.Current).SetTheme(value);}}
    public ICommand SaveCommand{get;}public ICommand AddProfileCommand{get;}public ICommand DuplicateCommand{get;}public ICommand DeleteProfileCommand{get;}public ICommand DefaultCommand{get;}public ICommand TestCommand{get;}public ICommand ApplyCommand{get;}public ICommand StopCommand{get;}public ICommand AddExeCommand{get;}public ICommand AddRunningCommand{get;}public ICommand AddMenuCommand{get;}public ICommand AddComponentCommand{get;}public ICommand RemoveAppCommand{get;}public ICommand LaunchCommand{get;}public ICommand ImportCommand{get;}public ICommand ExportCommand{get;}public ICommand RefreshAppsCommand{get;}
    EngineStatus? status;int poll;
    public MainViewModel(string directory)
    {
        store=new(directory);Data=store.Load();foreach(var p in Data.Profiles)Profiles.Add(p);
        foreach(var entry in Data.Applications)Applications.Add(Row(entry));
        SaveCommand=Command(()=>{Save();return Task.CompletedTask;});
        AddProfileCommand=Command(()=>{Selected=new(){Name="新配置"};Page="代理配置";EditorDirty=true;return Task.CompletedTask;});
        DuplicateCommand=Command(()=>{var original=Build();string pass=Password;EditorDirty=false;Selected=original with{Id=Guid.NewGuid(),Name=original.Name+" 副本",CredentialReference=""};Password=pass;EditorDirty=true;return Task.CompletedTask;});
        DeleteProfileCommand=Command(()=>{if(selected!=null){if(status?.Running==true&&status.ActiveName==selected.Name)throw new IOException("请先停止代理或应用其他配置。");if(MessageBox.Show("删除此配置及本机保存的密码？","删除配置",MessageBoxButton.YesNo)==MessageBoxResult.Yes){store.DeletePassword(selected.Id);Profiles.Remove(selected);selected=null;Selected=Profiles.FirstOrDefault();if(Profiles.Count==0)LoadProfile();Persist();}}return Task.CompletedTask;});
        DefaultCommand=Command(()=>{if(selected!=null&&Profiles.Any(p=>p.Id==selected.Id)){Data.Settings.DefaultProfileId=selected.Id;Persist();Message="已设为默认配置。";}return Task.CompletedTask;});
        TestCommand=Command(async()=>{Results.Clear();var result=await Upstream.Probe(new(Build(),Password,0));foreach(var step in result.Steps)Results.Add((step.Success?"通过":"提示")+" · "+step.Name+"："+step.Detail);Message=result.Success?"TCP 测试通过。UDP 与实际软件兼容性请分别查看。":"测试失败，当前代理未改变。";});
        ApplyCommand=Command(Apply);
        StopCommand=Command(async()=>{await PipeProtocol.Call("stop");Pending=true;Message="代理已停止，软件后续连接恢复正常联网。";await RefreshStatus();});
        AddExeCommand=Command(async()=>{var d=new OpenFileDialog{Filter="程序|*.exe"};if(d.ShowDialog()==true)Add(new(Path.GetFileNameWithoutExtension(d.FileName),await Discovery.Identify(d.FileName)));});
        AddRunningCommand=Command(async()=>Pick(await Discovery.Running(),"选择正在运行的软件"));
        AddMenuCommand=Command(async()=>Pick(await Discovery.StartMenu(),"选择开始菜单软件"));
        AddComponentCommand=Command(async()=>{if(SelectedSoftware==null)throw new IOException("请先选择软件条目。");var d=new OpenFileDialog{Filter="辅助程序|*.exe",Multiselect=true};if(d.ShowDialog()==true){foreach(string path in d.FileNames){var c=await Discovery.Identify(path);if(!SelectedSoftware.Entry.Components.Any(x=>x.Path.Equals(c.Path,StringComparison.OrdinalIgnoreCase)))SelectedSoftware.Entry.Components.Add(c);}SelectedSoftware.Changed();AppsChanged();}});
        RemoveAppCommand=Command(()=>{if(SelectedSoftware!=null){Applications.Remove(SelectedSoftware);AppsChanged();}return Task.CompletedTask;});
        LaunchCommand=Command(async()=>{if(SelectedSoftware==null)throw new IOException("请先选择软件。");await Discovery.Launch(SelectedSoftware.Entry.Components.First());Message="已请求打开软件；已有连接保留原网络路径。";});
        ImportCommand=Command(Import);ExportCommand=Command(Export);RefreshAppsCommand=Command(RefreshSoftware);
        selected=Profiles.FirstOrDefault(p=>p.Id==Data.Settings.DefaultProfileId)??Profiles.FirstOrDefault();LoadProfile();
    }
    ICommand Command(Func<Task> action)=>new ActionCommand(async()=>{if(Busy)return;Busy=true;try{await action();}catch(Exception ex){Message=ex.Message;}finally{Busy=false;}});
    void Persist(){Data.Profiles=Profiles.ToList();Data.Applications=Applications.Select(x=>x.Entry).ToList();store.Save(Data);}
    void AppsChanged(){Persist();Pending=true;}
    SoftwareRow Row(ApplicationEntry entry)
    {
        var row=new SoftwareRow(entry,AppsChanged);
        try{using var icon=System.Drawing.Icon.ExtractAssociatedIcon(entry.Components.First().Path);if(icon!=null){row.Icon=System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle,Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());row.Icon.Freeze();}}catch(Exception){}
        return row;
    }
    void Add(Candidate c)
    {
        if(Applications.Any(x=>x.Entry.Components.Any(y=>y.Path.Equals(c.Path,StringComparison.OrdinalIgnoreCase))))throw new IOException("该程序已经添加。");
        var row=Row(new(){Name=c.Name,Components=[c.Component]});Applications.Add(row);SelectedSoftware=row;AppsChanged();Page="软件";
    }
    void Pick(List<Candidate> candidates,string title){var dialog=new Picker(candidates){Title=title,Owner=Application.Current.MainWindow};if(dialog.ShowDialog()==true&&dialog.Selected!=null)Add(dialog.Selected);}
    void LoadProfile()
    {
        loading=true;
        try{var p=selected??new ProxyProfile();ProfileName=p.Name;Host=p.Host;Port=p.Port.ToString();Protocol=p.Protocol.ToString();Username=p.Username;Authentication=p.Authentication;Timeout=p.TimeoutSeconds.ToString();TestUrl=p.TestUrl;try{Password=store.ReadPassword(p);}catch(Exception ex){Password="";Message=ex.Message;}EditorDirty=false;Notify(nameof(Selected));}
        finally{loading=false;}
    }
    ProxyProfile Build()
    {
        if(!int.TryParse(Port,out int n)||!int.TryParse(Timeout,out int t)||!Enum.TryParse<ProxyProtocol>(Protocol,out var protocol))throw new IOException("请检查端口、超时和代理协议。");
        if(Authentication&&Password.Length==0)throw new IOException("请输入代理密码。");
        return((selected??new ProxyProfile()) with{Name=ProfileName,Host=Host,Port=n,Protocol=protocol,Username=Username,Authentication=Authentication,TimeoutSeconds=t,TestUrl=TestUrl}).Normalize();
    }
    void Save()
    {
        var p=Build();p=p with{CredentialReference=p.Authentication?store.SavePassword(p.Id,Password):""};
        int index=Profiles.ToList().FindIndex(x=>x.Id==p.Id);loading=true;
        try{if(index<0)Profiles.Add(p);else Profiles[index]=p;selected=p;}finally{loading=false;}
        Persist();LoadProfile();Message="配置已保存，点击应用后生效。";
    }
    async Task Apply()
    {
        if(selected==null||EditorDirty||!Profiles.Any(p=>p.Id==selected.Id))throw new IOException("请先保存代理配置。");
        var paths=new List<string>();var rows=Applications.Where(x=>x.Enabled).ToList();
        foreach(var row in rows)foreach(var component in row.Entry.Components)paths.Add(await Discovery.Resolve(component));
        if(rows.Count==0 && status?.Running==true){await UpdateRules(paths,0);Pending=false;Message="所有软件的新连接已恢复系统原有路径；已有代理连接自然结束。";return;}
        var activation=new Activation(selected,store.ReadPassword(selected),paths,rows.Count).Validate();
        var result=await PipeProtocol.Call("apply",activation);Update(result.Status!);Pending=false;Message="已应用。生效前已有的直连连接需由软件重连。";
    }
    async Task UpdateRules(List<string> paths,int count)
    {
        using var timeout=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response=await PipeProtocol.Call("rules",ct:timeout.Token,paths:paths,appCount:count);Update(response.Status!);
    }
    public async Task RefreshStatus()
    {
        try{var result=await PipeProtocol.Call("status");Update(result.Status!);if(++poll%15==0)await RefreshSoftware();}
        catch(Exception ex){Runtime="后台服务未连接";ActiveText=ex.Message;status=null;}
    }
    void Update(EngineStatus value)
    {
        status=value;Runtime=value.Running?"代理运行中 · "+value.SelectedApps+" 个软件":"代理未启用";
        if(value.Running && value.AppliedPaths!=null){var expected=Applications.Where(x=>x.Enabled).SelectMany(x=>x.Entry.Components).Select(x=>x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);Pending=!expected.SetEquals(value.AppliedPaths);}
        ActiveText=(value.ActiveName.Length==0?"当前配置：无":"当前配置："+value.ActiveName)+" · "+value.Detail;
        Events.Clear();foreach(var e in value.Events.Reverse())Events.Add(e);
        Flows.Clear();foreach(var f in value.Flows)Flows.Add(f);
        var owners=new Dictionary<uint,string>();foreach(var process in Process.GetProcesses())using(process)try{if(process.MainModule?.FileName is string path)owners[(uint)process.Id]=path;}catch(Exception){}
        foreach(var row in Applications){var paths=row.Entry.Components.Select(c=>c.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);row.Connections=value.Flows.Count(x=>owners.TryGetValue(x.Pid,out var path)&&paths.Contains(path));}
    }
    public async Task RefreshSoftware()
    {
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var p in Process.GetProcesses())using(p)try{if(p.MainModule?.FileName is string path)paths.Add(path);}catch(Exception){}
        var replacements=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in Applications)try{
            var resolved=new List<string>();
            for(int i=0;i<row.Entry.Components.Count;i++){
                var c=row.Entry.Components[i];string path=await Discovery.Resolve(c);resolved.Add(path);
                if(!path.Equals(c.Path,StringComparison.OrdinalIgnoreCase)){replacements[c.Path]=path;row.Entry.Components[i]=c with{Path=path};row.Changed();}
            }
            row.State=resolved.Any(paths.Contains)?"运行中":"未运行";
        }catch(Exception ex){row.State=ex.Message;}
        if(replacements.Count>0){
            Persist();
            if(status?.Running==true&&status.AppliedPaths!=null){
                var updated=status.AppliedPaths.Select(p=>replacements.GetValueOrDefault(p,p)).ToList();
                if(!updated.SequenceEqual(status.AppliedPaths,StringComparer.OrdinalIgnoreCase))await UpdateRules(updated,status.SelectedApps);
            }
        }
    }
    async Task Import()
    {
        var d=new OpenFileDialog{Filter="GPT Proxy Bridge 配置|profiles.json|JSON|*.json"};if(d.ShowDialog()!=true)return;
        var imported=await Task.Run(()=>ConfigurationTransfer.ImportLegacy(d.FileName,store));
        foreach(var p in imported.Profiles)Profiles.Add(p);
        Persist();Message=$"已导入 {imported.Profiles.Count} 个配置，{imported.MissingSecrets} 个配置需要重新填写密码。原文件未修改。";
    }
    Task Export()
    {
        var d=new SaveFileDialog{Filter="JSON|*.json",FileName="App-Proxy-Bridge-profiles.json"};
        if(d.ShowDialog()==true){File.WriteAllText(d.FileName,ConfigurationTransfer.Export(Data));Message="已导出，不含密码；包含代理地址和软件路径。";}
        return Task.CompletedTask;
    }
    public async Task<bool> StopForExit()
    {
        if(Busy){Message="正在处理，请稍候退出。";return false;}
        try{await PipeProtocol.Call("stop");return true;}catch(Exception ex){return MessageBox.Show("无法连接后台服务："+ex.Message+"\n服务可能仍在代理。仅退出界面？","退出",MessageBoxButton.YesNo)==MessageBoxResult.Yes;}
    }
}
