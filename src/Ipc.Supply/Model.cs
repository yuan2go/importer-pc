using Ipc.Platform;
namespace Ipc.Supply;

public sealed class FaultInjection
{
    public string? CrashAt { get; set; }
    public bool FailT1 { get; set; }
    public bool FailT3 { get; set; }
    public Action<string>? Hook { get; set; }
    public void Hit(string point)
    {
        Hook?.Invoke(point);
        if (CrashAt == point) Environment.FailFast("SIMULATION crash window " + point);
        if ((point == "T1" && FailT1) || (point == "T3" && FailT3)) throw new IOException("SIMULATION injected store failure at " + point);
    }
}
