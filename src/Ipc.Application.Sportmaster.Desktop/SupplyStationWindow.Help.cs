using System.Windows;
using System.Windows.Input;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    StationHelpWindow? helpWindow;
    void InitializeHelp()
    {
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Help, (_, _) => OpenHelp(),
            (_, e) => e.CanExecute = !closing && !closed));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Help, Key.F1, ModifierKeys.None));
    }
    void Help_Click(object sender, RoutedEventArgs e) => OpenHelp();
    void OpenHelp()
    {
        if (closing || closed) return;
        if (helpWindow is { IsVisible: true }) { helpWindow.Activate(); return; }
        try
        {
            var window = new StationHelpWindow(ViewModel.Application, () => ViewModel.IsManagementMode) { Owner = this };
            helpWindow = window;
            window.Closed += (_, _) => { if (ReferenceEquals(helpWindow, window)) helpWindow = null; RestoreBarcodeFocus(); };
            window.Show();
        }
        catch (Exception error)
        {
            ViewModel.Message = "使用帮助暂时无法打开：" + ChineseInterfaceText.ParameterMessage(DiagnosticText.Protect(error.Message));
        }
    }
}
