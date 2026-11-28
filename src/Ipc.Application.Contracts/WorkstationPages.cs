namespace Ipc.Application.Contracts;

public static class WorkstationPages
{
    // Identity for legacy configuration callers that have no composed flow.
    internal const string StationConfigurationId = "sportmaster";
    public const string Classic = "classic";
    public const string SupplyStation = "supply-station";
    public const string ToteStation = "tote-station";
    public const string MetadataKey = "Ipc.DefaultWorkstationPage";

    public static bool IsAllowed(string applicationId, string page) => applicationId switch
    {
        StationConfigurationId => page is SupplyStation or ToteStation,
        "direct-plc" or "wcs-to-plc" => page == Classic,
        _ => false
    };
}
