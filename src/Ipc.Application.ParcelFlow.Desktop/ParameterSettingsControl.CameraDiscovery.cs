using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class ParameterSettingsControl
{
    void AddCameraDiscovery(ObjectPage page, ParameterEditorModel model)
    {
        var scan = new Button { Content = "扫描局域网相机", MinHeight = 44, Margin = new(12, 0, 0, 0) };
        AutomationProperties.SetAutomationId(scan, "CameraDiscovery.Open");
        var header = new DockPanel { Margin = new(0, 0, 0, 8) };
        var heading = (TextBlock)page.Body.Children[0];
        page.Body.Children.RemoveAt(0);
        heading.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(scan, Dock.Right);
        header.Children.Add(scan);
        header.Children.Add(heading);
        page.Body.Children.Insert(0, header);
        scan.Click += (_, _) =>
        {
            if (Window.GetWindow(this) is not { } owner || !ReferenceEquals(Model, model)) return;
            scan.IsEnabled = false;
            scan.Content = "正在扫描…";
            try { ShowCameraDiscovery(owner, model); }
            finally { scan.Content = "扫描局域网相机"; scan.IsEnabled = true; }
        };
    }

    sealed record DiscoveryEngine(string Id, string Label);

    void ShowCameraDiscovery(Window owner, ParameterEditorModel model)
    {
        var readers = model.Readers.Where(reader => reader.IsSimpleCamera && reader.Provider?.Implementation == "hikrobot-mvs-reader").ToArray();
        string Engine(CapabilityDraft reader) => ParameterEditorModel.CameraField(reader, "reader.engine");
        var engines = readers.Select(Engine).Distinct().Select(engine => new DiscoveryEngine(engine,
            engine == "mvid-native" ? "海康原生识码" : "软件识码")).ToArray();
        var window = new Window
        {
            Owner = owner, Title = "扫描局域网相机", WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 920, Height = 520, MinWidth = 740, MinHeight = 360, ShowInTaskbar = false
        };
        UiTypography.Apply(window);
        window.SetResourceReference(BackgroundProperty, "Background");
        var panel = new DockPanel();
        window.Content = Themed(new Border { Padding = new(20), Child = panel }, Border.BackgroundProperty, "Background");
        var top = new DockPanel { Margin = new(0, 0, 0, 12) };
        var again = new Button { Content = "重新扫描", MinHeight = 44, Margin = new(12, 0, 0, 0) };
        AutomationProperties.SetAutomationId(again, "CameraDiscovery.Rescan");
        DockPanel.SetDock(again, Dock.Right);
        top.Children.Add(again);
        var choice = new ComboBox { ItemsSource = engines, DisplayMemberPath = "Label", SelectedIndex = 0, MinHeight = 44,
            Visibility = engines.Length > 1 ? Visibility.Visible : Visibility.Collapsed };
        AutomationProperties.SetName(choice, "按识码方式扫描");
        DockPanel.SetDock(choice, Dock.Right);
        top.Children.Add(choice);
        var status = Text("");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        top.Children.Add(status);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        var close = new Button { Content = "关闭", IsCancel = true, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new(0, 12, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom);
        panel.Children.Add(close);
        var rows = new StackPanel();
        panel.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        string Bound(DiscoveredCamera camera)
        {
            if (camera.HasIpConflict) return "地址冲突，请先修改相机 IP";
            var sides = readers.Where(reader => ParameterEditorModel.CameraField(reader, "device.ip") == camera.Ip ||
                ParameterEditorModel.CameraField(reader, "device.serial") == camera.Serial).Select(reader => reader.Role == "left" ? "左相机" : "右相机").ToArray();
            return sides.Length == 0 ? "未绑定" : "已绑定" + string.Join("、", sides);
        }
        Grid Columns()
        {
            var row = new Grid();
            foreach (var width in new[] { 130, 126, 132, 174, 0 })
                row.ColumnDefinitions.Add(new() { Width = width == 0 ? new(1, GridUnitType.Star) : new(width) });
            return row;
        }
        static void Cell(Grid row, UIElement content, int column) { Grid.SetColumn(content, column); row.Children.Add(content); }
        bool scanning = false;
        async Task Scan()
        {
            if (scanning) return;
            rows.Children.Clear();
            if (choice.SelectedItem is not DiscoveryEngine target)
            { status.Text = "当前设置没有可扫描的海康相机。"; again.IsEnabled = false; return; }
            scanning = true; again.IsEnabled = choice.IsEnabled = false;
            status.Text = "正在扫描…";
            try
            {
                var cameras = await model.CameraDiscoverer!(target.Id);
                if (!window.IsVisible) return;
                status.Text = cameras.Length == 0 ? "没有发现相机。请检查网线、供电和电脑网段。" :
                    $"已发现 {cameras.Length} 台海康相机。扫描不会修改相机设置。";
                if (cameras.Length == 0) return;
                var header = Columns();
                header.Margin = new(16, 0, 16, 0);
                foreach (var (label, index) in new[] { "IPv4 地址", "相机型号", "序列号", "当前状态" }.Select((label, index) => (label, index)))
                    Cell(header, Text(label, true), index);
                rows.Children.Add(header);
                foreach (var camera in cameras)
                {
                    var row = Columns();
                    foreach (var (value, index) in new[] { camera.Ip, camera.Model, camera.Serial, Bound(camera) }.Select((value, index) => (value, index)))
                    {
                        var cell = Text(value); cell.VerticalAlignment = VerticalAlignment.Center; cell.Margin = new(0, 2, 8, 2);
                        if (index == 3 && camera.HasIpConflict) cell.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
                        Cell(row, cell, index);
                    }
                    var actions = new WrapPanel();
                    foreach (var side in new[] { "left", "right" })
                    {
                        var reader = readers.SingleOrDefault(reader => reader.Role == side);
                        var blocked = camera.HasIpConflict ? "地址冲突，请先修改相机 IP。" :
                            reader is null || Engine(reader) != target.Id ? "请按本位识码方式重新扫描。" :
                            model.OtherCameraOwns(reader, camera) ? "另一侧已绑定此相机。请先清空另一侧的 IP。" : "";
                        var bind = new Button { Content = side == "left" ? "设为左相机" : "设为右相机", MinHeight = 44,
                            IsEnabled = blocked.Length == 0, Margin = new(0, 2, 4, 2), ToolTip = blocked.Length == 0 ? null : blocked };
                        ToolTipService.SetShowOnDisabled(bind, true);
                        bind.Click += (_, _) =>
                        {
                            try
                            {
                                if (!IsEnabled || !ReferenceEquals(Model, model)) throw new InvalidOperationException("编辑授权已结束，请重新验证后再选择。");
                                model.BindDiscoveredCamera(reader!, camera, target.Id); window.Close();
                            }
                            catch (InvalidOperationException error) { status.Text = error.Message; }
                        };
                        actions.Children.Add(bind);
                    }
                    Cell(row, actions, 4);
                    rows.Children.Add(Card(row));
                }
            }
            catch (Exception error)
            {
                status.Text = ChineseInterfaceText.ParameterMessage(error.Message, "扫描未完成。请检查网络和相机驱动后再试。");
            }
            finally { scanning = false; again.IsEnabled = choice.IsEnabled = true; }
        }
        again.Click += async (_, _) => await Scan();
        choice.SelectionChanged += async (_, _) => { if (window.IsVisible) await Scan(); };
        window.Loaded += async (_, _) => { close.Focus(); await Scan(); };
        window.ShowDialog();
    }
}
