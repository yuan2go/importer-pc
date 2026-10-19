using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    void OperatorLogin_Click(object sender, RoutedEventArgs e)
    {
        var body = new StackPanel { Margin = new(20) };
        body.SetBinding(IsEnabledProperty, new Binding(nameof(ViewModel.CanStartOperation)));
        body.Children.Add(new TextBlock { Text = "操作员账号" });
        var subject = new TextBox(); subject.SetBinding(TextBox.TextProperty, new Binding(nameof(ViewModel.OperatorSubject)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); body.Children.Add(subject);
        body.Children.Add(new TextBlock { Text = "操作员口令" }); var proof = new PasswordBox(); body.Children.Add(proof);
        var actions = new WrapPanel(); var login = new Button { Content = "验证身份" }; var logout = new Button { Content = "登出" };
        login.Click += async (_, _) => { var value = proof.Password; proof.Clear(); await ViewModel.RunAsync(() => ViewModel.LoginAsync(value), "认证操作员"); };
        logout.Click += async (_, _) => await ViewModel.RunAsync(ViewModel.LogoutAsync, "登出");
        actions.Children.Add(login); actions.Children.Add(logout); body.Children.Add(actions);
        var status = new TextBlock(); status.SetBinding(TextBlock.TextProperty, new Binding(nameof(ViewModel.Session))); body.Children.Add(status);
        var feedback = new TextBlock(); feedback.SetBinding(TextBlock.TextProperty, new Binding(nameof(ViewModel.OperatorFeedback))); body.Children.Add(feedback);
        new Window { Owner = this, DataContext = ViewModel, Title = "操作员身份", Width = 520, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = body }.Show();
    }
    void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        var body = new StackPanel { Margin = new(20) };
        foreach (var property in new[] { nameof(ViewModel.Station), nameof(ViewModel.TopologyStatus), nameof(ViewModel.Session), nameof(ViewModel.ConfigurationState), nameof(ViewModel.Health), nameof(ViewModel.BlockingReasons), nameof(ViewModel.AuxiliaryState), nameof(ViewModel.ReadStatus), nameof(ViewModel.Message) })
        {
            var text = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, Margin = new(0, 0, 0, 12) };
            text.SetBinding(TextBox.TextProperty, new Binding(property)); body.Children.Add(text);
        }
        new Window { Owner = this, Title = "站位与设备诊断 · 只读", DataContext = ViewModel, Width = 760, Height = 560, MinWidth = 520, MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.Show();
    }
}
