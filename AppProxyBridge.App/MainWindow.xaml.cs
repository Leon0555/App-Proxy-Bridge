using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace AppProxyBridge.App;
public partial class MainWindow : Window
{
    readonly MainViewModel vm;bool passwordUpdating;
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();this.vm=vm;DataContext=vm;PasswordInput.Password=vm.Password;
        vm.PropertyChanged+=Changed;Closing+=(_,e)=>{if(Application.Current is App app&&!app.Exiting){e.Cancel=true;Hide();}};
        Closed+=(_,_)=>vm.PropertyChanged-=Changed;
    }
    void Changed(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(vm.Password)){passwordUpdating=true;PasswordInput.Password=vm.Password;passwordUpdating=false;}}
    void PasswordChanged(object sender,RoutedEventArgs e){if(!passwordUpdating&&DataContext is MainViewModel model)model.Password=PasswordInput.Password;}
    void Navigate(object sender,RoutedEventArgs e){vm.Page=(string)((Button)sender).Tag;}
}
