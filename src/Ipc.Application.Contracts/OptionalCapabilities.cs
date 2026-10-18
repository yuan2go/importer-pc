namespace Ipc.Application.Contracts;

public sealed record OperatorIdentity(string Subject, string DisplayName, string Authority, string SessionId,
    DateTimeOffset AuthenticatedAt, bool Simulation);
public sealed record ImagePolicy(CapabilityBinding Camera, CapabilityBinding Uploader, int MaximumBytes = 4194304);
public sealed record ParcelImage(string CaptureRequestId, string AdmissionId, string RelativeFile, string MediaType,
    int Length, string Sha256, string? UploadedUri = null);
public interface IOperatorAuthenticator : ICapabilityPort
{
    Task<OperatorIdentity?> AuthenticateAsync(string subject, string proof, CancellationToken token);
}
public interface IImageCamera : ICapabilityPort
{
    Task<StageReply> CaptureAsync(FrozenRequest request, CancellationToken token);
    Task<StageReply?> QueryAsync(FrozenRequest original, CancellationToken token);
}
public interface IImageUploader : ICapabilityPort
{
    Task<StageReply> UploadAsync(FrozenRequest request, ReadOnlyMemory<byte> content, CancellationToken token);
    Task<StageReply?> QueryAsync(FrozenRequest original, CancellationToken token);
}
