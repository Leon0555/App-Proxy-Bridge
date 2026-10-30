using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AppProxyBridge.App;

public enum DashboardKind { Application, Entry, Profile }
public sealed record DashboardNode(string Key, DashboardKind Kind, Guid? Id, string Title, string Status, string Detail, Guid? TargetId = null);
public sealed record DashboardLink(string From, string To, bool Active);
public sealed record DashboardSnapshot(IReadOnlyList<DashboardNode> Nodes, IReadOnlyList<DashboardLink> Links, string Summary);

public partial class DashboardView : UserControl
{
    const double NodeWidth = 205, NodeHeight = 78, Step = 96, Top = 38;
    readonly Dictionary<string, (Button Button, TextBlock Title, TextBlock Status)> controls = [];
    readonly List<(Path Shape, string From, string To, bool Active)> linkPaths = [];
    readonly List<(Path Shape, string Source)> sharedHighlights = [];
    DashboardSnapshot? current;
    string? selectedKey;
    string structure = "";
    public event Action<DashboardNode>? NavigateRequested;

    public DashboardView() => InitializeComponent();

    public void Update(DashboardSnapshot snapshot)
    {
        current = snapshot; Summary.Text = snapshot.Summary;
        string signature = string.Join("|", snapshot.Nodes.Select(n => n.Key + ":" + n.Kind)) + ";" + string.Join("|", snapshot.Links.Select(l => l.From + ">" + l.To + ":" + l.Active));
        if (signature != structure) { structure = signature; Rebuild(snapshot); }
        foreach (var node in snapshot.Nodes) if (controls.TryGetValue(node.Key, out var c)) {
            c.Title.Text = node.Title; c.Status.Text = node.Status;
            c.Button.ToolTip = node.Title + " · " + node.Status;
            AutomationProperties.SetName(c.Button, node.Title + "，" + node.Status);
        }
        ShowDetails();
    }

    void Rebuild(DashboardSnapshot snapshot)
    {
        TopologyCanvas.Children.Clear(); controls.Clear(); linkPaths.Clear(); sharedHighlights.Clear();
        var positions = new Dictionary<string, Point>();
        var groups = new[] { DashboardKind.Application, DashboardKind.Entry, DashboardKind.Profile };
        var labels = new[] { "软件", "本地入口", "代理配置" };
        for (int column = 0; column < groups.Length; column++) {
            double x = 16 + column * 240;
            var heading = new TextBlock { Text = labels[column], FontSize = 12, FontWeight = FontWeights.SemiBold };
            heading.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            Canvas.SetLeft(heading, x); Canvas.SetTop(heading, 10); TopologyCanvas.Children.Add(heading);
            var nodes = snapshot.Nodes.Where(n => n.Kind == groups[column]).ToArray();
            if (nodes.Length == 0) {
                if (column == 0 || column == 2) {
                    var empty = new Button { Content = column == 0 ? "前往软件" : "前往代理配置", Width = 120, HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = column == 0 ? "前往软件页面添加" : "前往代理配置页面添加" };
                    var kind = column == 0 ? DashboardKind.Application : DashboardKind.Profile;
                    empty.Click += (_, _) => NavigateRequested?.Invoke(new DashboardNode("empty", kind, null, "", "", ""));
                    Canvas.SetLeft(empty, x + 4); Canvas.SetTop(empty, Top + 18); TopologyCanvas.Children.Add(empty);
                } else {
                    var empty = new TextBlock { Text = "暂无入口", FontSize = 12 };
                    empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                    Canvas.SetLeft(empty, x + 4); Canvas.SetTop(empty, Top + 18); TopologyCanvas.Children.Add(empty);
                }
            }
            for (int i = 0; i < nodes.Length; i++) positions[nodes[i].Key] = new Point(x, Top + i * Step);
        }
        int max = groups.Max(kind => snapshot.Nodes.Count(n => n.Kind == kind));
        TopologyCanvas.Width = 716; TopologyCanvas.Height = Math.Max(160, Top + max * Step + 12);
        var sharedLinks = snapshot.Links.Where(l => l.To == "entry:shared" && !l.Active && positions.ContainsKey(l.From) && positions.ContainsKey(l.To)).ToArray();
        if (sharedLinks.Length > 1) {
            var destination = positions["entry:shared"]; double endX = destination.X, endY = destination.Y + NodeHeight / 2;
            double trunkX = endX - 17; var starts = sharedLinks.Select(l => positions[l.From]).ToArray();
            var geometry = new StreamGeometry(); using (var context = geometry.Open()) {
                foreach (var point in starts) { context.BeginFigure(new Point(point.X + NodeWidth, point.Y + NodeHeight / 2), false, false); context.LineTo(new Point(trunkX, point.Y + NodeHeight / 2), true, false); }
                double firstY = Math.Min(endY, starts.Min(p => p.Y + NodeHeight / 2)); double lastY = Math.Max(endY, starts.Max(p => p.Y + NodeHeight / 2));
                context.BeginFigure(new Point(trunkX, firstY), false, false); context.LineTo(new Point(trunkX, lastY), true, false);
                context.BeginFigure(new Point(trunkX, endY), false, false); context.LineTo(new Point(endX, endY), true, false);
            }
            geometry.Freeze(); var trunk = new Path { Data = geometry, StrokeThickness = 1.4, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false };
            trunk.SetResourceReference(Shape.StrokeProperty, "FocusBrush"); TopologyCanvas.Children.Add(trunk);
            linkPaths.Add((trunk, "", "entry:shared", false));
            foreach (var link in sharedLinks) {
                var point = positions[link.From]; double sourceY = point.Y + NodeHeight / 2;
                var highlightGeometry = new StreamGeometry();
                using (var context = highlightGeometry.Open()) {
                    context.BeginFigure(new Point(point.X + NodeWidth, sourceY), false, false);
                    context.LineTo(new Point(trunkX, sourceY), true, false);
                    context.LineTo(new Point(trunkX, endY), true, false);
                    context.LineTo(new Point(endX, endY), true, false);
                }
                highlightGeometry.Freeze();
                var highlight = new Path { Data = highlightGeometry, StrokeThickness = 2.2, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                highlight.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
                TopologyCanvas.Children.Add(highlight); sharedHighlights.Add((highlight, link.From));
            }
        }
        foreach (var link in snapshot.Links) if (!(sharedLinks.Length > 1 && sharedLinks.Contains(link)) && positions.TryGetValue(link.From, out var from) && positions.TryGetValue(link.To, out var to)) {
            var start = new Point(from.X + NodeWidth, from.Y + NodeHeight / 2); var end = new Point(to.X, to.Y + NodeHeight / 2);
            var geometry = new StreamGeometry(); using (var context = geometry.Open()) { context.BeginFigure(start, false, false); double mid = (start.X + end.X) / 2; context.BezierTo(new Point(mid, start.Y), new Point(mid, end.Y), end, true, false); }
            geometry.Freeze(); var path = new Path { Data = geometry, StrokeThickness = link.Active ? 2 : 1.4, IsHitTestVisible = false };
            path.SetResourceReference(Shape.StrokeProperty, link.Active ? "AccentBrush" : "FocusBrush");
            if (!link.Active) path.StrokeDashArray = new DoubleCollection { 3, 3 };
            TopologyCanvas.Children.Add(path); linkPaths.Add((path, link.From, link.To, link.Active));
        }
        foreach (var node in snapshot.Nodes) {
            if (!positions.TryGetValue(node.Key, out var point)) continue;
            var title = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
            var status = new TextBlock { FontSize = 12, Margin = new Thickness(0, 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
            status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var stack = new StackPanel(); stack.Children.Add(title); stack.Children.Add(status);
            var button = new Button { Style = (Style)FindResource("TopologyNode"), Content = stack, Tag = node.Key };
            button.Click += (_, _) => { selectedKey = (string)button.Tag; ShowDetails(); };
            Canvas.SetLeft(button, point.X); Canvas.SetTop(button, point.Y); TopologyCanvas.Children.Add(button);
            controls[node.Key] = (button, title, status);
        }
    }

    void ShowDetails()
    {
        var node = current?.Nodes.FirstOrDefault(n => n.Key == selectedKey);
        foreach (var pair in controls) pair.Value.Button.SetResourceReference(Button.BackgroundProperty, pair.Key == selectedKey ? "SelectionBrush" : "CardBrush");
        foreach (var link in linkPaths) {
            bool highlighted = node != null && (link.From == node.Key || link.To == node.Key);
            link.Shape.SetResourceReference(Shape.StrokeProperty, highlighted || link.Active ? "AccentBrush" : "FocusBrush");
            link.Shape.StrokeThickness = highlighted ? 2.2 : link.Active ? 2 : 1.4;
        }
        foreach (var highlight in sharedHighlights) highlight.Shape.Visibility = node?.Key == highlight.Source ? Visibility.Visible : Visibility.Collapsed;
        if (node == null) { selectedKey = null; DetailPanel.Visibility = Visibility.Collapsed; return; }
        DetailPanel.Visibility = Visibility.Visible; DetailTitle.Text = node.Title; DetailText.Text = node.Detail;
        JumpButton.Content = node.Kind == DashboardKind.Application ? "打开软件" : "打开代理配置";
    }
    void JumpClick(object sender, RoutedEventArgs e)
    {
        var node = current?.Nodes.FirstOrDefault(n => n.Key == selectedKey);
        if (node != null) NavigateRequested?.Invoke(node);
    }
}

