namespace Ipc.Supply;

// Typed application dispatch only. Command.Kind remains the original persisted string.
public enum SupplyAction { Unknown, Login, Logout, Pause, Resume, ClearOverload, OpenContext, Submit, ScanRetained, ClearSplit, CancelPrepared, CancelDraft, CheckClose, CancelClose, ConfirmClose, SetFixtureFault, SetSyntheticBlock, ClearSyntheticBlock, ReviewObservations, RecordReconciliation, SaveConfig, ActivateConfig, ClearQuarantine, AckAlarm }

public static class SupplyCommands
{
    public const string Login = "Login";
    public const string Logout = "Logout";
    public const string Pause = "Pause";
    public const string Resume = "Resume";
    public const string ClearOverload = "ClearOverload";
    public const string OpenContext = "OpenContext";
    public const string Submit = "Submit";
    public const string ScanRetained = "ScanRetained";
    public const string ClearSplit = "ClearSplit";
    public const string CancelPrepared = "CancelPrepared";
    public const string CancelDraft = "CancelDraft";
    public const string CheckClose = "CheckClose";
    public const string CancelClose = "CancelClose";
    public const string ConfirmClose = "ConfirmClose";
    public const string SetFixtureFault = "SetFixtureFault";
    public const string SetSyntheticBlock = "SetSyntheticBlock";
    public const string ClearSyntheticBlock = "ClearSyntheticBlock";
    public const string ReviewObservations = "ReviewObservations";
    public const string RecordReconciliation = "RecordReconciliation";
    public const string SaveConfig = "SaveConfig";
    public const string ActivateConfig = "ActivateConfig";
    public const string ClearQuarantine = "ClearQuarantine";
    public const string AckAlarm = "AckAlarm";

    public static bool TryParse(string? kind, out SupplyAction action)
    {
        action = kind switch
        {
            Login => SupplyAction.Login,
            Logout => SupplyAction.Logout,
            Pause => SupplyAction.Pause,
            Resume => SupplyAction.Resume,
            ClearOverload => SupplyAction.ClearOverload,
            OpenContext => SupplyAction.OpenContext,
            Submit => SupplyAction.Submit,
            ScanRetained => SupplyAction.ScanRetained,
            ClearSplit => SupplyAction.ClearSplit,
            CancelPrepared => SupplyAction.CancelPrepared,
            CancelDraft => SupplyAction.CancelDraft,
            CheckClose => SupplyAction.CheckClose,
            CancelClose => SupplyAction.CancelClose,
            ConfirmClose => SupplyAction.ConfirmClose,
            SetFixtureFault => SupplyAction.SetFixtureFault,
            SetSyntheticBlock => SupplyAction.SetSyntheticBlock,
            ClearSyntheticBlock => SupplyAction.ClearSyntheticBlock,
            ReviewObservations => SupplyAction.ReviewObservations,
            RecordReconciliation => SupplyAction.RecordReconciliation,
            SaveConfig => SupplyAction.SaveConfig,
            ActivateConfig => SupplyAction.ActivateConfig,
            ClearQuarantine => SupplyAction.ClearQuarantine,
            AckAlarm => SupplyAction.AckAlarm,
            _ => SupplyAction.Unknown
        };
        return action != SupplyAction.Unknown;
    }
    public static bool IsPriority(string? kind) => TryParse(kind, out var action) && action is SupplyAction.Pause or SupplyAction.Logout or SupplyAction.ClearOverload;

    public static bool RequiresMaintenance(SupplyAction action) => action is
        SupplyAction.ClearOverload or SupplyAction.CancelPrepared or SupplyAction.SetFixtureFault or
        SupplyAction.SetSyntheticBlock or SupplyAction.ClearSyntheticBlock or SupplyAction.ReviewObservations or
        SupplyAction.RecordReconciliation or SupplyAction.SaveConfig or SupplyAction.ActivateConfig or SupplyAction.ClearQuarantine;

    // Shared eligibility only; application guards still enforce live health, pending effects,
    // command identities, quantity and evidence. A window cannot grant business permission.
    public static bool SessionReady(SupplyAction action, StationSession? session) => action switch
    {
        SupplyAction.OpenContext => session is null || session.Status is SessionStatus.Closed or SessionStatus.Rejected,
        SupplyAction.CheckClose => session?.Status == SessionStatus.Active,
        SupplyAction.CancelClose => session?.Status == SessionStatus.AwaitingShortageDecision,
        SupplyAction.ConfirmClose => session?.Status == SessionStatus.AwaitingShortageDecision && session.CloseToken is not null,
        _ => true
    };
}

// Guard categories are internal behavior, never serialized command aliases.
public enum SupplyGuardAction { Open, Admit, Submit, Close, ConfirmClose }
