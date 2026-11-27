namespace Ipc.Application.Contracts;

public sealed record DiscoveredCamera(string Ip, string Serial, string Model, string Firmware, bool HasIpConflict);
