using System.Windows;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class ToteEndSupplyConfirmWindow : Window
{
    public ToteEndSupplyConfirmWindow(IToteSessionController tote)
    {
        InitializeComponent();
        UiTypography.Apply(this);
        Counts.Text = UiText.Format("Tote.CountSummary", tote.DispatchedCount, tote.PlannedCount);
        Missing.Text = UiText.Format("Tote.Missing", tote.MissingCount);
        Loaded += (_, _) => CancelAction.Focus();
    }

    void ForceEnd_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
