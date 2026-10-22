using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AppProxyBridge.App;
public sealed class Picker : Window
{
    public Candidate? Selected{get;private set;}
    public Picker(List<Candidate> candidates)
    {
        Width=880;Height=560;MinWidth=700;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"BackgroundBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var dock=new DockPanel{Margin=new Thickness(20)};Content=dock;
        var search=new TextBox{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(search,Dock.Top);dock.Children.Add(search);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);dock.Children.Add(buttons);
        var add=new Button{Content="添加软件",IsDefault=true};var cancel=new Button{Content="取消",IsCancel=true};buttons.Children.Add(add);buttons.Children.Add(cancel);
        var view=new ListCollectionView(candidates);var list=new ListBox{ItemsSource=view};
        list.SetResourceReference(BackgroundProperty,"CardBrush");list.SetResourceReference(ForegroundProperty,"TextBrush");
        dock.Children.Add(list);
        search.TextChanged+=(_,_)=>{view.Filter=x=>((Candidate)x).ToString().Contains(search.Text,System.StringComparison.OrdinalIgnoreCase);view.Refresh();};
        void Pick(){if(list.SelectedItem is Candidate item){Selected=item;DialogResult=true;}}
        add.Click+=(_,_)=>Pick();list.MouseDoubleClick+=(_,_)=>Pick();
    }
}
