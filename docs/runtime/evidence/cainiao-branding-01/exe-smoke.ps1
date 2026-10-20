$ErrorActionPreference = 'Stop'
$executable = 'D:/releases/cainiao-branding-03/desktop/Ipc.Desktop.exe'
$info = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
if ($info.ProductName -ne '菜鸟供包台' -or $info.FileDescription -ne '菜鸟供包台') { throw 'Wrong EXE product metadata' }
Add-Type -AssemblyName System.Drawing
$actual = [Drawing.Icon]::ExtractAssociatedIcon($executable).ToBitmap()
$expectedIcon = [Drawing.Icon]::new('D:/projects/importer-pc/src/Ipc.Desktop/Assets/importer-pc.ico', $actual.Width, $actual.Height)
$expected = $expectedIcon.ToBitmap()
try {
    for ($x = 0; $x -lt $actual.Width; $x++) {
        for ($y = 0; $y -lt $actual.Height; $y++) {
            if ($actual.GetPixel($x, $y).ToArgb() -ne $expected.GetPixel($x, $y).ToArgb()) { throw 'Published EXE icon differs from supplied artwork icon' }
        }
    }
} finally { $actual.Dispose(); $expected.Dispose(); $expectedIcon.Dispose() }
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class BrandSmokeWindow {
    private delegate bool EnumCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    public static IntPtr Find(int processId, string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, _) => {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != processId) return true;
            var text = new StringBuilder(1024); GetWindowText(window, text, text.Capacity);
            if (text.ToString() != title) return true;
            found = window; return false;
        }, IntPtr.Zero);
        return found;
    }
    public static bool Close(IntPtr window) => PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
}
"@
$process = Start-Process -FilePath $executable -ArgumentList '--setup' -WorkingDirectory 'D:/releases/cainiao-branding-03' -WindowStyle Hidden -PassThru
[void]$process.Handle
try {
    for ($attempt = 0; $attempt -lt 200; $attempt++) {
        $process.Refresh()
        if ($process.HasExited) { throw 'EXE exited before showing setup' }
        $setupWindow = [BrandSmokeWindow]::Find($process.Id, '安装与部署向导 · 菜鸟供包台')
        if ($setupWindow -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($setupWindow -eq [IntPtr]::Zero) { throw ('Expected setup HWND missing; owner title: ' + $process.MainWindowTitle) }
    $title = '安装与部署向导 · 菜鸟供包台'
    if (-not [BrandSmokeWindow]::Close($setupWindow) -or -not $process.WaitForExit(10000)) { throw 'Setup did not close normally' }
    if ($process.ExitCode -ne 0) { throw ('Setup exit code: ' + $process.ExitCode) }
    [ordered]@{ Status='PASS'; ProductName=$info.ProductName; FileDescription=$info.FileDescription; NativeWindowTitle=$title; Icon='Exact pixel match to generated ICO'; Setup='Opened and closed without installing, provisioning or device access'; ExitCode=$process.ExitCode } | ConvertTo-Json
} finally { $process.Dispose() }
