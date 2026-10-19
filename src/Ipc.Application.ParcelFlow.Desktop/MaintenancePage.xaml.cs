using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace Ipc.Application.ParcelFlow.Desktop;
public partial class MaintenancePage : UserControl
{
    CapabilityDraft? selected;
    public MaintenancePage() => InitializeComponent();
    async void Load_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.LoadCapabilitiesAsync); }
    async void ConfirmationLoad_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.LoadConfigurationConfirmationAsync); }
    async void ConfirmationSave_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ConfirmConfigurationAsync); }
    async void ValidateFull_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ValidateAdvancedConfigurationAsync); }
    async void SaveFull_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.SaveAdvancedConfigurationAsync); }
    async void ValidateStructured_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ValidateStructuredConfigurationAsync); }
    async void Advanced_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.SwitchToAdvancedConfigurationAsync); }
    async void StructuredFromJson_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.LoadStructuredFromJsonAsync); }
    async void AddInstance_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.AddCapabilityInstanceAsync); }
    async void RemoveInstance_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.RemoveCapabilityInstanceAsync(Instances.SelectedItem as CapabilityDraft)); }
    async void Uninstall_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.UninstallSelectedPackageAsync); }
    async void RecoverInstall_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.RecoverOriginalInstallationAsync); }
    async void Manage_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ManageExtensionAsync); }
    async void Archive_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ArchiveHistoryAsync); }
    async void Identity_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.AcknowledgeIdentityAsync); }
    async void Receipts_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.LoadReceiptResponsibilitiesAsync); }
    async void ReceiptQuery_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ReconcileReceiptAsync); }
    async void Save_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.SaveCapabilitiesAsync); }
    async void Reconnect_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ReconnectAsync); }
    async void Overload_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.CommandAsync("AcknowledgeOverload")); }
    async void Backup_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.BackupAsync); }
    async void Authorize_Click(object s, RoutedEventArgs e) { var proof = MaintenanceProof.Password; MaintenanceProof.Clear(); if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.AuthorizeMaintenanceAsync(proof)); }
    async void Install_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.InstallAsync); }
    async void Upload_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ContinueUploadAsync); }
    async void Abandon_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.AbandonAsync); }
    void Reader_Changed(object sender, SelectionChangedEventArgs e) => SelectDraft((ReaderInstances.SelectedItem as ReaderConfigurationDraft)?.Capability);
    void Instance_Changed(object sender, SelectionChangedEventArgs e) => SelectDraft(Instances.SelectedItem as CapabilityDraft);
    void SelectDraft(CapabilityDraft? draft)
    {
        if (selected is not null) selected.PropertyChanged -= ProviderChanged;
        selected = draft;
        if (selected is not null) selected.PropertyChanged += ProviderChanged;
        RenderParameters();
    }
    void ProviderChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(CapabilityDraft.Provider)) RenderParameters(); }
    void RenderParameters()
    {
        if (ParameterForm is null || ReaderParameterForm is null) return;
        ParameterForm.Children.Clear(); ReaderParameterForm.Children.Clear();
        var form = selected?.Kind == "observation" ? ReaderParameterForm : ParameterForm;
        if (selected is not null) form.Children.Add(new TextBlock { Text = selected.ProviderIdentity, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });
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
            row.Children.Add(input); form.Children.Add(row);
        }
    }
}
