namespace Ipc.Supply;

// Stable machine codes. Localized text belongs to presentation; persisted values stay unchanged.
public static class SupplyReasonCodes
{
    public const string OperatorPaused = "OperatorPaused";
    public const string WindowArea = "WindowArea";
    public const string Overload = "Overload";
    public const string StorageFailure = "StorageFailure";
    public const string Stopping = "Stopping";
    public const string RestoreQuarantine = "RestoreQuarantine";
    public const string IdentityUnavailable = "IdentityUnavailable";
    public const string WcsHealth = "WcsHealth";
    public const string OutcomeUnknown = "OutcomeUnknown";
    public const string OriginalIntegrationUnavailable = "OriginalIntegrationUnavailable";
    public const string OriginalActorExpired = "OriginalActorExpired";
    public const string OriginalBindingNotActive = "OriginalBindingNotActive";
    public const string OriginalImplementationNotActive = "OriginalImplementationNotActive";
    public const string SessionChanged = "SessionChanged";
    public const string OperationRevisionOrOccupancyChanged = "OperationRevisionOrOccupancyChanged";
    public const string CloseBarrier = "CloseBarrier";
    public const string NoActiveBoxOrCloseBarrier = "NoActiveBoxOrCloseBarrier";
    public const string PendingExternalEffect = "PendingExternalEffect";
    public const string PhysicalOccupancyNotClear = "PhysicalOccupancyNotClear";
    public const string EntityPermitExpired = "EntityPermitExpired";
    public const string OperationNotReady = "OperationNotReady";
    public const string EntityNotOccupied = "EntityNotOccupied";
    public const string QuantityAuthorityMissing = "QuantityAuthorityMissing";
    public const string RetainedCountIncomplete = "RetainedCountIncomplete";
    public const string ObservationReview = "ObservationReview";
    public const string LocalDispositionRequired = "LocalDispositionRequired";
    public const string ConflictingEvidence = "ConflictingEvidence";
    public const string ReceiptPersistenceUncertain = "ReceiptPersistenceUncertain";
    public const string ConfigPending = "ConfigPending";
    public const string UnexpiredMaintenanceIdentityRequired = "UnexpiredMaintenanceIdentityRequired";
    public const string SelectConfigurationRevision = "SelectConfigurationRevision";
    public const string StaleOrInvalidConfigurationRevision = "StaleOrInvalidConfigurationRevision";
    public const string PendingExternalResponsibility = "PendingExternalResponsibility";
    public const string CloseCurrentBoxBeforeActivation = "CloseCurrentBoxBeforeActivation";
    public const string EntityOrRecoveryResponsibility = "EntityOrRecoveryResponsibility";
    public const string NoActiveOverload = "NoActiveOverload";
    public const string AdmittedObservationsDraining = "AdmittedObservationsDraining";
    public const string ReconcileSentOrTerminateProvenUndeliveredPrepared = "ReconcileSentOrTerminateProvenUndeliveredPrepared";
    public const string OnlyPreparedCanTerminateLocally = "OnlyPreparedCanTerminateLocally";
    public const string NoDurableNonDeliveryProof = "NoDurableNonDeliveryProof";
}
