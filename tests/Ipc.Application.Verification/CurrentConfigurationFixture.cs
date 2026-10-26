using Ipc.Platform;

// Synthetic CURRENT envelopes explicitly declare their current startup evidence.
// Historical refusal fixtures must use the raw DTO, never this helper.
static class CurrentConfigurationFixture
{
    public static ConfigurationRevision<T> Create<T>(long Revision,T Effective,T? Pending,string Status,string? Failure,
        ConfigurationActivationAttempt? Activation=null, ConfigurationEffectiveConfirmation? Confirmation=null,
        ConfigurationInitialRepair? InitialRepair=null,bool Confirmed=true) where T:class =>
        new(Revision,Effective,Pending,Status,Failure,Activation,
            Confirmation ?? new("Seed",Wire.Hash(Wire.Encode(Effective)),!Confirmed,Confirmed ? "Confirmed" : "Required",null,null,
                Confirmed ? [new("synthetic-current-startup",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"ResourcesStarted")] : [],
                ConfirmedBy:Confirmed ? "synthetic-fixture" : null,ConfirmedAt:Confirmed ? DateTimeOffset.UtcNow : null),InitialRepair);
}
