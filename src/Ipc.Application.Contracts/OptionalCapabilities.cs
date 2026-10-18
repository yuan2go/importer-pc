using Ipc.Platform;
namespace Ipc.Application.Contracts;

public enum UploadStrategy { BeforeMain, Attached }
public sealed record ImagePolicy(CapabilityBinding Camera, CapabilityBinding Uploader, int MaximumBytes = 4194304,
    UploadStrategy Strategy = UploadStrategy.BeforeMain, long TotalBytes = 33554432, int MaximumPending = 32);
public sealed record ParcelImage(string CaptureRequestId, string AdmissionId, string RelativeFile, string MediaType,
    int Length, string Sha256, string? UploadedUri = null);
public interface IImageCamera : Ipc.Platform.IImageCamera<FrozenRequest, StageReply> { }
public interface IImageUploader : Ipc.Platform.IImageUploader<FrozenRequest, StageReply> { }
public interface ILoginExtension { IOperatorAuthenticator Create(CapabilityBinding binding); }
public interface ICameraExtension { IImageCamera Create(CapabilityBinding binding, string temporaryDirectory); }
public interface IUploadExtension { IImageUploader Create(CapabilityBinding binding); }
public interface ICapabilityDeclaration { CapabilityRequirements Requirements { get; } }
