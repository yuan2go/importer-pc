# Native geometry assertions require a desktop large enough for their exact clients.
# This changes only the disposable GitHub runner's current display mode, not registry defaults.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'This display preparation is restricted to disposable GitHub runners' }
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WpfCiDisplay {
 [StructLayout(LayoutKind.Explicit, Size=220, CharSet=CharSet.Unicode)]
 public struct Mode {
  [FieldOffset(68)] public ushort Size;
  [FieldOffset(72)] public uint Fields;
  [FieldOffset(172)] public uint Width;
  [FieldOffset(176)] public uint Height;
 }
 [DllImport("user32.dll", CharSet=CharSet.Unicode)]
 public static extern bool EnumDisplaySettings(string device, int index, ref Mode mode);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)]
 public static extern int ChangeDisplaySettings(ref Mode mode, uint flags);
 [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
}
'@
$mode = [WpfCiDisplay+Mode]::new()
$mode.Size = 220
if (-not [WpfCiDisplay]::EnumDisplaySettings($null, -1, [ref]$mode)) { throw 'Cannot read native runner display mode' }
Write-Host "Original runner display: $($mode.Width)x$($mode.Height)"
if ($mode.Width -lt 1920 -or $mode.Height -lt 1080) {
 $mode.Width = 1920; $mode.Height = 1080; $mode.Fields = 0x180000
 $result = [WpfCiDisplay]::ChangeDisplaySettings([ref]$mode, 0)
 if ($result -ne 0) { throw "Runner display driver refused 1920x1080: $result" }
}
if ([WpfCiDisplay]::GetSystemMetrics(0) -lt 1920 -or [WpfCiDisplay]::GetSystemMetrics(1) -lt 1080) { throw 'Native runner desktop remains too small' }
Write-Host 'Native runner desktop supports the unchanged geometry assertions.'
