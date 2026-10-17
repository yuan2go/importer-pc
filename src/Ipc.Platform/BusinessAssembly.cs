namespace Ipc.Platform;

// Neutral build-time business registration. No customer role, transport or service locator.
public sealed record BusinessCommand(string Id, long ExpectedRevision, string Kind, string? Owner = null, string? Value = null);
public sealed record BusinessReceipt(string Status, string Reason, long Revision);
public sealed record BusinessProjection(Guid RuntimeId, long Revision, string State, Dictionary<string, string> Facts);
public sealed record BusinessLaunch(string ConfigurationDirectory, string DataRoot, string? View, string? Layout);
public sealed record BusinessPresentation(string[] Views, string[] Layouts);
public interface IBusinessApplication
{
    Task<BusinessReceipt> CommandAsync(BusinessCommand command);
    Task<BusinessProjection> QueryAsync();
}
public interface IBusinessRuntime : IRuntimeModule
{
    IBusinessApplication Application { get; }
    BusinessPresentation Presentation { get; }
}
public sealed class BusinessAssembly(string businessId, Func<BusinessLaunch, IBusinessRuntime> factory)
{
    IBusinessRuntime? instance;
    public IBusinessRuntime Create(BusinessLaunch launch)
    {
        if (instance is not null) throw new InvalidOperationException("One business owner per assembly; views reuse its application");
        var created = factory(launch);
        if (created.Id != businessId) throw new InvalidDataException("Build registration and business implementation disagree");
        if (launch.View is not null && !created.Presentation.Views.Contains(launch.View) || launch.Layout is not null && !created.Presentation.Layouts.Contains(launch.Layout))
            throw new InvalidDataException("View/layout does not belong to the selected business");
        return instance = created;
    }
}
