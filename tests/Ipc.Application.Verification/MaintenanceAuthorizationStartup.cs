using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Principal;
using Ipc.Platform;

static class MaintenanceAuthorizationStartup
{
    sealed record Receipt(string UserHash, bool AdminEnabled);
    static string[] Prefix => Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        ? [Assembly.GetExecutingAssembly().Location] : [];
    public static async Task Probe(string[] args)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var identity = WindowsIdentity.GetCurrent();
        using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(10000);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var accepted = new byte[1]; await pipe.ReadExactlyAsync(accepted, budget.Token);
        if (accepted[0] != 1) throw new IOException("Probe was not authenticated");
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new Receipt(Wire.Hash(identity.User!.Value),
            new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))));
        var ack = new byte[1]; await pipe.ReadExactlyAsync(ack, budget.Token);
        if (ack[0] != 1) throw new IOException("Missing probe acknowledgment");
    }
    public static async Task OrdinaryChild(string[] args)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var identity = WindowsIdentity.GetCurrent();
        MachineRuntimePolicy.ValidateRuntimeAccount(identity.User!.Value, identity.User.Value, true, identity.Claims);
        var name = "ipc-maintenance-probe-" + Wire.Id();
        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var connected = pipe.WaitForConnectionAsync(budget.Token);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Environment.CurrentDirectory };
        foreach (var argument in Prefix.Concat(new[] { "--maintenance-authority-probe", name })) start.ArgumentList.Add(argument);
        using var process = await WindowsMaintenanceProcess.StartAsync(start, pipe, connected, budget.Token);
        await connected;
        await pipe.WriteAsync(new byte[] { 1 }, budget.Token);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        var receipt = Wire.Decode<Receipt>((await reader.ReadLineAsync(budget.Token))!);
        if (!receipt.AdminEnabled || receipt.UserHash != Wire.Hash(identity.User.Value)) throw new UnauthorizedAccessException("Maintenance probe lacks same-user authority");
        await pipe.WriteAsync(new byte[] { 1 }, budget.Token); await pipe.FlushAsync(budget.Token);
        await process.WaitForExitAsync(budget.Token);
        if (process.ExitCode != 0) throw new IOException("Maintenance probe failed");
        await RejectUnprivilegedHelper(args[2], Path.GetDirectoryName(args[1])!);
        ExtensionStore.AtomicWrite(args[1], receipt);
    }
    static async Task RejectUnprivilegedHelper(string helper, string root)
    {
        var name = Wire.Id() + Wire.Id(); var path = Path.Combine(root, "negative-request.bin");
        using var initiator = Process.GetCurrentProcess();
        ProtectedDraftPayload.Write(path, System.Text.Json.JsonSerializer.Serialize(new { Action = "ConfigureManagementPassword", Pipe = name,
            InitiatorProcessId = initiator.Id, InitiatorStartedTicks = initiator.StartTime.ToUniversalTime().Ticks }), false);
        try
        {
            using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var connected = pipe.WaitForConnectionAsync(budget.Token);
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(helper)! };
            foreach (var arg in new[] { "--startup-command", path, Wire.FileHash(path) }) start.ArgumentList.Add(arg);
            using var child = Process.Start(start) ?? throw new IOException("Negative helper did not start");
            await connected;
            try { await child.WaitForExitAsync(budget.Token).WaitAsync(TimeSpan.FromMilliseconds(150)); throw new Exception("Helper exited before client authentication"); }
            catch (TimeoutException) { }
            await pipe.WriteAsync(new byte[] { 1 }, budget.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var response = System.Text.Json.JsonDocument.Parse((await reader.ReadLineAsync(budget.Token))!);
            await child.WaitForExitAsync(budget.Token);
            if (child.ExitCode != 2 || response.RootElement.GetProperty("Status").GetString() != "Failed" ||
                !response.RootElement.GetProperty("Error").GetString()!.Contains("Extension writes require", StringComparison.Ordinal))
                throw new Exception("Unprivileged production helper did not return its explicit authority rejection");
        }
        finally { File.Delete(path); }
    }
    public static void Run(string root, string workspace)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows token verification required");
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException("Start fixture from the existing Windows maintenance session");
        var receipt = Path.Combine(root, "maintenance-authority.json");
        var helper = Path.Combine(workspace, "src/Ipc.Assembly/bin/Release/net10.0/Ipc.Assembly.exe");
        using var child = OrdinaryRuntimeProcess.Start(Environment.ProcessPath!, [..Prefix, "--ordinary-maintenance-child", receipt, helper], Environment.CurrentDirectory);
        if (child.Wait(60000) != 0 || !File.Exists(receipt)) throw new Exception("Restricted runtime could not complete real maintenance authority round trip");
        var result = Wire.Decode<Receipt>(File.ReadAllText(receipt));
        if (!result.AdminEnabled || result.UserHash != Wire.Hash(identity.User!.Value)) throw new Exception("Maintenance authority changed user or remained restricted");
        Console.WriteLine("PASS MAINTENANCE-START real NORMALUSER runtime -> desktop runas -> same-user administrator helper -> authenticated pipe acknowledgment; no deployment/password/device writes");
        Console.WriteLine("PASS MAINTENANCE-START real production helper waits for client authentication; unprivileged maintenance returns Failed/exit 2, without work or CLR crash");
    }
}
