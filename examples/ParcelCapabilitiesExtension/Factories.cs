using Ipc.Application.Contracts;
namespace Ipc.Reference.ParcelCapabilitiesExtension;
public sealed class WeightFactory : IWeightExtension
{ public IWeightSource Create(CapabilityBinding binding) => new Ipc.Drivers.ReferenceWeightSource(binding); }
public sealed class PlcFactory : IPlcExtension
{ public IPlcPort Create(CapabilityBinding binding) => new Ipc.Drivers.ReferencePlc(binding, binding.Parameters["peer-root"]); }
public sealed class WcsFactory : IWcsExtension
{ public IWcsPort Create(CapabilityBinding binding) => new Ipc.Integrations.ReferenceWcs(binding, binding.Parameters["peer-root"]); }
