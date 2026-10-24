namespace Ipc.Presentation.Wpf;

public static partial class UiTypography
{
    public const double Body = 16;
    public const double Label = 14;
    public const double Diagnostic = 12;
    public const double Group = 16;
    public const double Position = 18;
    public const double PageTitle = 20;
    public const double Status = 16;
    public const double Placeholder = 18;
    public const double Exception = 20;
    public const double Barcode = 28;
    public const double BarcodeCompact = 24;
    public const double Slot = 64;
    public const double SlotCompact = 44;
    public const double Log = 14;

    public static double SlotSize(string value, bool compact) => (value.Length, compact) switch
    { (<= 6, false) => Slot, (<= 6, true) => SlotCompact, (<= 16, false) => 40, (<= 16, true) => Barcode, (_, false) => Barcode, _ => BarcodeCompact };
}
