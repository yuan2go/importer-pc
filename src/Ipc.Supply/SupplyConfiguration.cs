using Ipc.Platform;

namespace Ipc.Supply;

public static class SupplyConfiguration
{
    public static IReadOnlyList<string> Validate(StationConfig s)
    {
        var e = ProductValidation.ValidateStationStructure(s).ToList();
        if (s.Devices is null || s.Devices.Any(x => x is null) || s.Integration is null) return e;
        if (s.Devices.Length != 2 || !s.Devices.Select(x => x.Role).Order().SequenceEqual(new[] { "left", "right" })) e.Add("Supply requires two unique left/right logical bindings");
        var requests = s.Extensions?.Ports?.SingleOrDefault(x => x.Port == "requests");
        if (s.Extensions?.Services?.Any(x => x.Enabled) == true && requests is null) e.Add("Supply requires a requests integration port");
        if (requests is not null && requests.Target != s.Integration) e.Add("Supply integration target must match its requests port");
        if (s.Mode == RunMode.Simulation)
        {
            if (s.Devices.Any(x => x.Driver is not ("manual" or "replay") && !x.Driver.StartsWith("extension:", StringComparison.Ordinal))) e.Add("Unknown simulation driver");
            if (!Uri.TryCreate(s.Integration.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.UserInfo != "" || uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "") e.Add("Fixture requires literal IPv4 loopback root HTTP endpoint");
            if (s.Integration.Contract != "SIMULATION-v1" || s.Policy != "SYNTHETIC-KEEP-v1" || s.FixtureDemand is < 1 or > 100000 || s.MaximumPackage is < 1 or > 10000) e.Add("Invalid synthetic policy/contract");
            if (s.Devices.Any(x => x.Driver == "replay" && string.IsNullOrWhiteSpace(x.ReplayFile))) e.Add("Replay file missing");
        }
        else e.Add("Production NotReady: approved SDK, WCS contract, quantity/entity policy and identity provider unavailable");
        return e;
    }
}
