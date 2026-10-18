using Ipc.Platform;

namespace Ipc.Desktop;

public sealed record ConfigurationFieldIssue(string Field, string Message)
{
    public override string ToString() => Field + "：" + Message;
}

// Locate errors with the same read-only validator as Preview. Each probe changes one
// field from the effective basis; no policy/range is reimplemented in the UI. Only
// errors present in the actual candidate and absent from the basis get a field label.
// Cross-field-only errors remain in the full summary instead of inventing a location.
public static class ConfigurationValidationPresentation
{
    public static ConfigurationFieldIssue[] Locate(StationConfig effective, StationConfig candidate,
        IReadOnlyList<string> actualErrors, Func<StationConfig, IReadOnlyList<string>> validate)
    {
        if (actualErrors.Count == 0) return [];
        var originalErrors = validate(effective).ToHashSet(StringComparer.Ordinal);
        var actual = actualErrors.ToHashSet(StringComparer.Ordinal);
        List<ConfigurationFieldIssue> issues = [];
        void Check(string field, StationConfig isolated)
        {
            foreach (var error in validate(isolated).Where(x => actual.Contains(x) && !originalErrors.Contains(x)))
                issues.Add(new(field, error));
        }
        void Value<T>(string field, T old, T next, Func<StationConfig, StationConfig> change)
        { if (!EqualityComparer<T>.Default.Equals(old, next)) Check(field, change(effective)); }
        StationConfig Target(IntegrationBinding target) => effective with
        {
            Integration = target,
            Extensions = effective.Extensions is { Ports: { } ports } deployment
                ? deployment with { Ports = ports.Select(x => x.Port == "requests" ? x with { Target = target } : x).ToArray() } : effective.Extensions
        };
        Value("StationId", effective.StationId, candidate.StationId, x => x with { StationId = candidate.StationId });
        Value("Version", effective.Version, candidate.Version, x => x with { Version = candidate.Version });
        Value("Endpoint", effective.Integration.Endpoint, candidate.Integration.Endpoint, _ => Target(effective.Integration with { Endpoint = candidate.Integration.Endpoint }));
        Value("BindingVersion", effective.Integration.Revision, candidate.Integration.Revision, _ => Target(effective.Integration with { Revision = candidate.Integration.Revision }));
        Value("Contract", effective.Integration.Contract, candidate.Integration.Contract, _ => Target(effective.Integration with { Contract = candidate.Integration.Contract }));
        Value("Policy", effective.Policy, candidate.Policy, x => x with { Policy = candidate.Policy });
        Value("Timeout", effective.TimeoutMs, candidate.TimeoutMs, x => x with { TimeoutMs = candidate.TimeoutMs });
        Value("EventCapacity", effective.EventCapacity, candidate.EventCapacity, x => x with { EventCapacity = candidate.EventCapacity });
        Value("CommandCapacity", effective.CommandCapacity, candidate.CommandCapacity, x => x with { CommandCapacity = candidate.CommandCapacity });
        Value("OutboxCapacity", effective.OutboxCapacity, candidate.OutboxCapacity, x => x with { OutboxCapacity = candidate.OutboxCapacity });
        Value("MinimumFree", effective.MinimumFreeMb, candidate.MinimumFreeMb, x => x with { MinimumFreeMb = candidate.MinimumFreeMb });
        Value("FixtureDemand", effective.FixtureDemand, candidate.FixtureDemand, x => x with { FixtureDemand = candidate.FixtureDemand });
        Value("MaximumPackage", effective.MaximumPackage, candidate.MaximumPackage, x => x with { MaximumPackage = candidate.MaximumPackage });
        foreach (var device in candidate.Devices)
        {
            var original = effective.Devices.FirstOrDefault(x => x.Role == device.Role);
            if (original is null || original == device) continue;
            Check("Devices." + device.Role, effective with { Devices = effective.Devices.Select(x => x.Role == device.Role ? device : x).ToArray() });
        }
        return issues.Distinct().ToArray();
    }
}
