using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace Ipc.Application.ParcelFlow.Desktop;
public partial class MaintenancePage : UserControl
{
    CapabilityDraft? selected;
    public MaintenancePage() => InitializeComponent();
    async void Load_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.LoadCapabilitiesAsync); }
    async void Save_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.SaveCapabilitiesAsync); }
    async void Reconnect_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ReconnectAsync); }
    async void Overload_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.CommandAsync("AcknowledgeOverload")); }
    async void Backup_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.BackupAsync); }
    async void Authorize_Click(object s, RoutedEventArgs e) { var proof = MaintenanceProof.Password; MaintenanceProof.Clear(); if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.AuthorizeMaintenanceAsync(proof)); }
    async void Install_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.InstallAsync); }
    async void Upload_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ContinueUploadAsync); }
    async void Abandon_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.AbandonAsync); }
    void Instance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (selected is not null) selected.PropertyChanged -= ProviderChanged;
        selected = Instances.SelectedItem as CapabilityDraft;
        if (selected is not null) selected.PropertyChanged += ProviderChanged;
        RenderParameters();
    }
    void ProviderChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(CapabilityDraft.Provider)) RenderParameters(); }
    void RenderParameters()
    {
        if (ParameterForm is null) return;
        ParameterForm.Children.Clear();
        foreach (var p in selected?.Parameters ?? [])
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(new TextBlock { Text = p.Field.Label + (p.Field.Required ? " *" : "") + " " + p.Field.Unit, Width = 270, VerticalAlignment = VerticalAlignment.Center });
            Control input;
            if (p.Field.Type is "choice" or "boolean")
            {
                var choice = new ComboBox { ItemsSource = p.Field.Type == "boolean" ? new[] { "true", "false" } : p.Field.Choices, Width = 310 };
                choice.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(p.Value)) { Source = p, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); input = choice;
            }
            else
            {
                var text = new TextBox { Width = 310, MaxLength = p.Field.MaximumLength, IsReadOnly = p.Field.ReadOnly };
                text.SetBinding(TextBox.TextProperty, new Binding(nameof(p.Value)) { Source = p, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); input = text;
            }
            input.ToolTip = p.Field.Description ?? p.Field.Type; input.IsEnabled = !p.Field.ReadOnly;
            row.Children.Add(input); ParameterForm.Children.Add(row);
        }
    }
}
