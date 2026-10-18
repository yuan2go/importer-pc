// Projection tests replace the visibility enum. WindowEventShim separately supplies
// an event/timer boundary for close-control tests; neither represents native WPF.
namespace System.Windows;
public enum Visibility { Visible, Hidden, Collapsed }
