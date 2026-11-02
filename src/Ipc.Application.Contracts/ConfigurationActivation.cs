using Ipc.Platform;
namespace Ipc.Application.Contracts;

// The review and the executing Application use the same whole-candidate rule.
// This predicts the mode only; execution still rechecks permission/CAS/drain.
public static class ConfigurationActivation
{
    public static bool RulesOnly(string application, ParcelConfiguration effective, ParcelConfiguration? pending, ParcelConfiguration candidate)
        => application == "sportmaster" && pending is null &&
           Wire.Encode(candidate with { BarcodeFilter = effective.BarcodeFilter }) == Wire.Encode(effective);
}
