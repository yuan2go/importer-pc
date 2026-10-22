using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using Ipc.Application.ParcelFlow.Desktop;

static partial class Program
{
    static async Task ClickManagementMenu(DependencyObject root, string action)
    {
        var menu = Descendants(root).OfType<ManagementModeMenu>().Single(x => x.IsVisible);
        var peer = new ButtonAutomationPeer(menu);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (menu.ContextMenu?.IsOpen != true) throw new InvalidOperationException("Native management menu did not open");
        var item = menu.ContextMenu.Items.OfType<MenuItem>().Single(x => Equals(x.Header, action) && x.IsEnabled);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.ContextMenu.IsOpen = false;
    }
}
