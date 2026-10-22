using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace AppProxyBridge.App;
public partial class App : Application
{
    readonly bool startDesktop;
    public App():this(true){}
    public App(bool startDesktop){this.startDesktop=startDesktop;}
    Mutex? mutex;EventWaitHandle? wake;Forms.NotifyIcon? tray;DispatcherTimer? timer;
    public bool Exiting{get;private set;}
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(!startDesktop)return;
        mutex=new(true,"Local\\AppProxyBridge.Instance",out bool first);wake=new(false,EventResetMode.AutoReset,"Local\\AppProxyBridge.Wake");
        if(!first){wake.Set();Shutdown();return;}
        try {
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AppProxyBridge");
            int index=Array.IndexOf(e.Args,"--data-dir");if(index>=0&&index+1<e.Args.Length)directory=Path.GetFullPath(e.Args[index+1]);
            var vm=new MainViewModel(directory);SetTheme(vm.Theme);
            MainWindow=new MainWindow(vm);MainWindow.Show();
            tray=new(){Text="App Proxy Bridge",Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??System.Drawing.SystemIcons.Application,Visible=true};
            tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(Show);
            var menu=new Forms.ContextMenuStrip();
            menu.Items.Add("打开 App Proxy Bridge",null,(_,_)=>Dispatcher.Invoke(Show));
            menu.Items.Add("停止代理",null,(_,_)=>Dispatcher.Invoke(()=>vm.StopCommand.Execute(null)));
            menu.Items.Add("退出",null,async(_,_)=>{if(await vm.StopForExit()){Exiting=true;Shutdown();}});
            tray.ContextMenuStrip=menu;
            timer=new(){Interval=TimeSpan.FromSeconds(2)};bool refreshing=false;
            timer.Tick+=async(_,_)=>{if(wake.WaitOne(0))Show();if(refreshing||vm.Busy)return;refreshing=true;try{await vm.RefreshStatus();}finally{refreshing=false;}};timer.Start();
            SystemEvents.UserPreferenceChanged+=ThemeChanged;
            _=vm.RefreshStatus();
        }catch(Exception ex){MessageBox.Show(ex.Message,"App Proxy Bridge 启动失败");Shutdown(1);}
    }
    void ThemeChanged(object sender,UserPreferenceChangedEventArgs e)=>Dispatcher.InvokeAsync(()=>{if(MainWindow?.DataContext is MainViewModel vm)SetTheme(vm.Theme);});
    void Show(){MainWindow.Show();MainWindow.WindowState=WindowState.Normal;MainWindow.Activate();}
    protected override void OnExit(ExitEventArgs e){timer?.Stop();tray?.Dispose();wake?.Dispose();mutex?.Dispose();SystemEvents.UserPreferenceChanged-=ThemeChanged;base.OnExit(e);}
    public void SetTheme(string theme)
    {
        bool dark=theme=="Dark"||theme=="System"&&(int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1)==0;
        string[] colors=dark?["#1C1C1F","#242427","#2C2C30","#F1F1F4","#AFB0BA","#45454D","#3785F7"]:["#F6F6F8","#ECECF0","#FFFFFF","#202126","#626570","#DCDDE3","#146EF5"];
        string[] keys=["BackgroundBrush","SidebarBrush","CardBrush","TextBrush","MutedBrush","LineBrush","AccentBrush"];
        for(int i=0;i<keys.Length;i++)Resources[keys[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        if(SystemParameters.HighContrast){Resources["BackgroundBrush"]=SystemColors.WindowBrush;Resources["SidebarBrush"]=SystemColors.WindowBrush;Resources["CardBrush"]=SystemColors.WindowBrush;Resources["TextBrush"]=SystemColors.WindowTextBrush;Resources["MutedBrush"]=SystemColors.WindowTextBrush;Resources["AccentBrush"]=SystemColors.HighlightBrush;}
    }
}
