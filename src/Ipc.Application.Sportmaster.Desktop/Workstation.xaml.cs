using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Ipc.Application.ParcelFlow.Desktop;
namespace Ipc.Application.Sportmaster.Desktop;
public partial class Workstation : UserControl
{
    readonly ObservableCollection<PositionRow> rows = [];
    ParcelViewModel? observed;
    public Workstation()
    {
        InitializeComponent(); Positions.ItemsSource = rows;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) => { if (IsLoaded) Attach(); };
    }
    void Detach()
    {
        if (observed is not null) PropertyChangedEventManager.RemoveHandler(observed, PositionsChanged, nameof(ParcelViewModel.PositionRows));
        observed = null;
    }
    void Attach()
    {
        Detach(); observed = DataContext as ParcelViewModel;
        if (observed is not null) PropertyChangedEventManager.AddHandler(observed, PositionsChanged, nameof(ParcelViewModel.PositionRows));
        UpdatePositions();
    }
    void PositionsChanged(object? sender, PropertyChangedEventArgs e) => UpdatePositions();
    void UpdatePositions()
    {
        var values = observed?.PositionRows ?? [];
        for (var i = 0; i < values.Length; i++)
        {
            var row = rows.FirstOrDefault(x => x.Value.Id == values[i].Id);
            if (row is null) { row = new(values[i]); rows.Insert(i, row); }
            else { var previous = rows.IndexOf(row); if (previous != i) rows.Move(previous, i); row.Update(values[i]); }
        }
        while (rows.Count > values.Length) rows.RemoveAt(rows.Count - 1);
    }
    // Stable view items keep native scroll, expansion and text selection across snapshot refreshes.
    sealed class PositionRow(PositionPresentation value) : INotifyPropertyChanged
    {
        public PositionPresentation Value { get; private set; } = value;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Update(PositionPresentation next)
        {
            if (Value == next) return;
            Value = next; PropertyChanged?.Invoke(this, new(nameof(Value)));
        }
    }
    void Position_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm && s is Button { Tag: string position }) vm.SelectedPositionId = position; }
    void New_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) vm.NewParcel(); }
    async void Emit_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => { vm.Emit(); return Task.CompletedTask; }, "模拟相机读码"); }
    async void Weight_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => { vm.EmitWeight(); return Task.CompletedTask; }, "同包裹模拟实测重量"); }
}
