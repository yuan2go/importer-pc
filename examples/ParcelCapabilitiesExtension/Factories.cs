using Ipc.Platform;
using Ipc.Application.Contracts;
namespace Ipc.Reference.ParcelCapabilitiesExtension;
public sealed class WeightFactory : IWeightExtension
{ public IWeightSource Create(CapabilityBinding binding) => new Ipc.Drivers.ReferenceWeightSource(binding); }
public sealed class PlcFactory : IPlcExtension
{ public IPlcPort Create(CapabilityBinding binding) => new Ipc.Drivers.ReferencePlc(binding, binding.Parameters["peer-root"]); }
public sealed class WcsFactory : IWcsExtension
{ public IWcsPort Create(CapabilityBinding binding) => new Ipc.Integrations.ReferenceWcs(binding, binding.Parameters["peer-root"]); }

public sealed class LoginFactory : ILoginExtension
{ public IOperatorAuthenticator Create(CapabilityBinding binding) => new Ipc.Components.ReferenceOperatorAuthenticator(binding); }
public sealed class CameraFactory : ICameraExtension
{ public IImageCamera Create(CapabilityBinding binding, string temporaryDirectory) => new Ipc.Drivers.ReferenceImageCamera(binding, temporaryDirectory); }
public sealed class UploadFactory : IUploadExtension
{ public IImageUploader Create(CapabilityBinding binding) => new Ipc.Components.ReferenceImageUploader(binding, binding.Parameters["peer-root"]); }
