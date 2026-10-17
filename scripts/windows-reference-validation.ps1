param(
    [Parameter(Mandatory)][string]$PackageRoot,
    [Parameter(Mandatory)][string]$Output,
    [switch]$RunUi,
    [switch]$ProvisionCurrentAccountForRunnerSmoke
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Actual Windows execution is required; this script does not emulate WPF' }
$release = (Resolve-Path -LiteralPath $PackageRoot).Path
if (Test-Path -LiteralPath $Output) { throw 'Evidence directory must be new; previous evidence retained' }
New-Item -ItemType Directory -Path $Output | Out-Null
$destination = (Resolve-Path -LiteralPath $Output).Path
$package = Get-Content -LiteralPath (Join-Path $release 'PACKAGE.json') -Raw | ConvertFrom-Json
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $administrator = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $accountSid = $identity.User.Value
} finally { $identity.Dispose() }
$environmentReport = [ordered]@{ TimeUtc=[DateTimeOffset]::UtcNow.ToString('O'); OS=[Environment]::OSVersion.VersionString;
    OSBuild=[Environment]::OSVersion.Version.Build; Architecture=[Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString();
    SessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId; UserInteractive=[Environment]::UserInteractive;
    AccountSid=$accountSid; Administrator=$administrator; Package=$package; ActualScreenshots=0;
    W01='NOT_RUN';W02='NOT_RUN';W03='NOT_RUN';W04='NOT_RUN';W05='NOT_RUN';W06='NOT_RUN';
    RealScanner='NOT_RUN';Touch='NOT_RUN';AcousticListening='NOT_RUN';NativeWpfSmoke='NOT_RUN';
    Note='Runner smoke is separate from Windows11/DPI/ordinary-account/physical-input/continuous-journey acceptance.' }
$fixture = $null; $client = $null
try {
    $listed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in Get-Content -LiteralPath (Join-Path $release 'SHA256SUMS')) {
        if ($line -notmatch '^([0-9a-fA-F]{64})  (.+)$') { throw 'Invalid SHA256SUMS record' }
        $hash = $Matches[1]; $relative = $Matches[2]
        if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|/)[.][.](/|$)|[\\:]' -or -not $listed.Add($relative)) { throw 'Unsafe/duplicate package path' }
        $file = Join-Path $release $relative
        if ((Get-Item -LiteralPath $file -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked package file' }
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $hash) { throw "Changed package file: $relative" }
    }
    $actual = @(Get-ChildItem -LiteralPath $release -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($release,$_.FullName).Replace('\','/') } | Where-Object { $_ -ne 'SHA256SUMS' })
    if ($actual.Count -ne $listed.Count -or @($actual | Where-Object { -not $listed.Contains($_) }).Count -ne 0) { throw 'Package file set differs from locked inventory' }
    $environmentReport['PackageReadback']='PASS'
    if (-not $RunUi) { return }
    if ($ProvisionCurrentAccountForRunnerSmoke) {
        if (-not $administrator) { throw 'Runner provisioning requires an already elevated authorized account; do not auto-approve UAC' }
        & (Join-Path $release 'provision-runtime.ps1') -RuntimeAccount "$env:USERDOMAIN\$env:USERNAME" | Out-File (Join-Path $destination 'provision.log')
        $environmentReport['OrdinaryAccountQualification']='NOT_RUN_ADMIN_RUNNER_ONLY'
    }
    Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,PresentationFramework
    Add-Type @'
using System; using System.Runtime.InteropServices;
public static class IpcScreenEvidence {
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr window, out Rect rect);
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
}
'@
    $dataRoot = Join-Path $destination 'synthetic-data'
    function Start-Native([string]$File,[string[]]$Arguments) {
        $start = [Diagnostics.ProcessStartInfo]::new($File); $start.UseShellExecute=$false
        foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
        return [Diagnostics.Process]::Start($start)
    }
    $fixture = Start-Native (Join-Path $release 'fixture/Ipc.Fixture.exe') @('--url','http://127.0.0.1:18743/','--data',(Join-Path $destination 'synthetic-receiver'))
    $client = Start-Native (Join-Path $release 'app/Ipc.Desktop.exe') @('--config',(Join-Path $release 'app/config'),'--data-root',$dataRoot)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($client.MainWindowHandle -eq [IntPtr]::Zero -and -not $client.HasExited -and $timer.Elapsed.TotalSeconds -lt 30) { Start-Sleep -Milliseconds 250; $client.Refresh() }
    if ($client.HasExited -or $client.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Actual WPF process did not expose a main window; preserve process/environment evidence' }
    $window = [Windows.Automation.AutomationElement]::FromHandle($client.MainWindowHandle)
    $environmentReport['WindowTitle']=$window.Current.Name
    $environmentReport['WindowDpi']=[IpcScreenEvidence]::GetDpiForWindow($client.MainWindowHandle)
    $rectangle = [IpcScreenEvidence+Rect]::new(); [void][IpcScreenEvidence]::GetClientRect($client.MainWindowHandle,[ref]$rectangle)
    $environmentReport['ClientPixels']=@($rectangle.Right-$rectangle.Left,$rectangle.Bottom-$rectangle.Top)
    $environmentReport['ClientDip']=@(($rectangle.Right-$rectangle.Left)*96/$environmentReport.WindowDpi,($rectangle.Bottom-$rectangle.Top)*96/$environmentReport.WindowDpi)
    function Capture-Window([string]$Name) {
        $bounds=$window.Current.BoundingRectangle
        if ($bounds.Width -le 0 -or $bounds.Height -le 0) { throw 'WPF window has no visible screen rectangle' }
        $bitmap=[Drawing.Bitmap]::new([int]$bounds.Width,[int]$bounds.Height); $graphics=[Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.CopyFromScreen([int]$bounds.Left,[int]$bounds.Top,0,0,$bitmap.Size);$bitmap.Save((Join-Path $destination $Name),[Drawing.Imaging.ImageFormat]::Png);$environmentReport.ActualScreenshots++ }
        finally { $graphics.Dispose();$bitmap.Dispose() }
    }
    Capture-Window '01-native-startup.png'
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'组件管理与恢复')
    $entry=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
    if ($null -eq $entry) { throw 'Core extension recovery entry is not discoverable in actual WPF' }
    $invoke=[Windows.Automation.InvokePattern]$entry.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern); $invoke.Invoke()
    Start-Sleep -Milliseconds 800
    Capture-Window '02-native-extension-management.png'
    $environmentReport['NativeWpfSmoke']='PASS_STARTUP_AND_CORE_MANAGEMENT_ONLY'
    $environmentReport['ProcessMetrics']=[ordered]@{WorkingSet=$client.WorkingSet64;PrivateBytes=$client.PrivateMemorySize64;Handles=$client.HandleCount;ElapsedSeconds=$timer.Elapsed.TotalSeconds}
    $environmentReport['UiAutomation']='Actual native WPF InvokePattern; no scanner/touch/listening claim'
} catch {
    $environmentReport['Failure']=$_.Exception.ToString()
    $environmentReport['NativeWpfSmoke']='FAIL_OR_ENVIRONMENT_BLOCKED'
    throw
} finally {
    # Harness shutdown only: synthetic isolated processes, no production device/WCS.
    # This is not evidence for graceful operator shutdown or Stop ownership qualification.
    foreach ($process in @($client,$fixture)) { if ($null -ne $process -and -not $process.HasExited) { $process.Kill($true);$process.WaitForExit() } }
    $environmentReport | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $destination 'windows-observations.json') -Encoding utf8
}
