using System.Globalization;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    static string StationMinute(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:00Z", CultureInfo.InvariantCulture);
    void ProjectStationObservation(ParcelInputSnapshot input, long sequence, SqliteTransaction tx, string? inheritedShift = null)
        => ProjectStationObservationCore(db, input, sequence, tx, inheritedShift ?? stationShiftId ?? "");
    static void ProjectStationObservationCore(SqliteConnection connection, ParcelInputSnapshot input, long sequence, SqliteTransaction tx, string shift)
    {
        if (input.Observation.InspectionSessionId is not null || input.Outcome == ParcelInputOutcome.Duplicate) return;
        var recognized = input.Observation.OperatorInput is null && input.Observation.Codes.Length > 0 ? 1 : 0;
        var scope = ""; var reason = "";
        if (input.Outcome == ParcelInputOutcome.Rejected)
        {
            scope = "recognition";
            reason = input.Observation.Codes.Length == 0 ? "NoBarcodeObservation" : input.BarcodeSelection?.Outcome switch
            {
                "AmbiguousCodes" => "MultipleCodes",
                "NoMatchingCode" or "ExcludedCodes" => "BarcodeRule",
                "InvalidPattern" or "FilterTimeout" or "JoinedCodeTooLong" => "BarcodeFilterFailure",
                _ => "OtherRejectedObservation"
            };
        }
        else if (input.Outcome == ParcelInputOutcome.Blocked) { scope = "admission"; reason = "AdmissionBlocked"; }
        using var c = connection.CreateCommand(); c.Transaction = tx;
        c.CommandText = "INSERT INTO station_metric_facts VALUES($id,'observation',$owner,$seq,$shift,$at,$minute,$position,$recognitions,0,0,0,0,0,$scope,$reason,$issues)";
        c.Parameters.AddWithValue("$id", "o." + sequence); c.Parameters.AddWithValue("$seq", sequence);
        c.Parameters.AddWithValue("$owner", input.OperationId ?? ""); c.Parameters.AddWithValue("$shift", shift);
        c.Parameters.AddWithValue("$at", input.Observation.ObservedAt.ToUniversalTime().ToString("O")); c.Parameters.AddWithValue("$minute", StationMinute(input.Observation.ObservedAt));
        c.Parameters.AddWithValue("$position", input.PositionId); c.Parameters.AddWithValue("$recognitions", recognized);
        c.Parameters.AddWithValue("$scope", scope); c.Parameters.AddWithValue("$reason", reason); c.Parameters.AddWithValue("$issues", reason.Length == 0 ? 0 : 1); c.ExecuteNonQuery();
    }
    void ProjectStationRequest(ParcelOperation op, SqliteTransaction tx, string? inheritedShift = null)
        => ProjectStationRequestCore(db, op, tx, inheritedShift ?? stationShiftId ?? "");
    static void ProjectStationRequestCore(SqliteConnection connection, ParcelOperation op, SqliteTransaction tx, string shift)
    {
        if (op.FrozenConfiguration.ApplicationId != "sportmaster" || op.DebugRequest is not null || op.OperatorDebugRequest is not null || op.Wcs is not { } stage) return;
        var attempted = stage.Attempts > 0;
        var reason = !attempted ? "" : stage.State switch
        {
            StageState.Conflict => "RcsConflict",
            StageState.Unknown when stage.Replies.LastOrDefault()?.SenderDelivery == SenderDeliveryEvidence.ConnectionRefusedBeforeBody => "RcsNotDelivered",
            StageState.Unknown when stage.LocalFailure == RequestLocalFailure.DeadlineExpired => "RcsTimeout",
            StageState.Unknown => "RcsUnknown",
            StageState.Rejected => "RcsRejected",
            _ => ""
        };
        using var c = connection.CreateCommand(); c.Transaction = tx;
        c.CommandText = """
            INSERT INTO station_metric_facts VALUES($id,'request',$owner,NULL,$shift,$at,$minute,$position,0,$requests,$assigned,$rejected,$unknown,$manual,$scope,$reason,$issues)
            ON CONFLICT(id) DO UPDATE SET requests=excluded.requests,assigned=excluded.assigned,rejected=excluded.rejected,
              unknown_count=excluded.unknown_count,manual_requests=excluded.manual_requests,reason_scope=excluded.reason_scope,reason_code=excluded.reason_code,issues=excluded.issues
            WHERE station_metric_facts.owner=excluded.owner AND station_metric_facts.kind=excluded.kind AND station_metric_facts.fact_at=excluded.fact_at AND station_metric_facts.position=excluded.position
            """;
        c.Parameters.AddWithValue("$id", "r." + stage.Request.Id); c.Parameters.AddWithValue("$owner", op.Id);
        c.Parameters.AddWithValue("$shift", shift); c.Parameters.AddWithValue("$at", stage.Request.PreparedAt.ToUniversalTime().ToString("O"));
        c.Parameters.AddWithValue("$minute", StationMinute(stage.Request.PreparedAt)); c.Parameters.AddWithValue("$position", op.PositionId ?? "main");
        c.Parameters.AddWithValue("$requests", attempted ? 1 : 0); c.Parameters.AddWithValue("$assigned", attempted && stage.State == StageState.Accepted && !string.IsNullOrWhiteSpace(op.Slot) ? 1 : 0);
        c.Parameters.AddWithValue("$rejected", attempted && stage.State == StageState.Rejected ? 1 : 0); c.Parameters.AddWithValue("$unknown", attempted && stage.State is StageState.Unknown or StageState.Conflict ? 1 : 0);
        c.Parameters.AddWithValue("$manual", attempted && op.OperatorInput is not null ? 1 : 0); c.Parameters.AddWithValue("$scope", reason.Length == 0 ? "" : "rcs");
        c.Parameters.AddWithValue("$reason", reason); c.Parameters.AddWithValue("$issues", reason.Length == 0 ? 0 : 1);
        if (c.ExecuteNonQuery() != 1) throw new InvalidDataException("原请求统计归属发生冲突，保留原责任");
    }
    // Aggregates already contain every live fact. Retention removes only the
    // indexed detail, in the source-deletion transaction; never add it again.
    void RetireStationFacts(string predicate, string owner, SqliteTransaction tx)
    {
        using var c = db.CreateCommand(); c.Transaction = tx;
        c.CommandText = "DELETE FROM station_metric_facts WHERE " + predicate; c.Parameters.AddWithValue("$owner", owner); c.ExecuteNonQuery();
    }
    void RetainOperationStatistics(ParcelOperation op, SqliteTransaction tx)
    {
        RetireStationFacts("owner=$owner", op.Id, tx);
        RetainRecognitions("json_extract(detail,'$.OperationId')=$owner", op.Id, tx);
    }
    void RetainInputStatistics(string ingress, SqliteTransaction tx)
        => RetainRecognitions("seq IN (SELECT seq FROM parcel_ingress_audits WHERE input=$owner)", ingress, tx);
    void RetainRecognitions(string predicate, string owner, SqliteTransaction tx)
        => RetireStationFacts("source_seq IN (SELECT seq FROM parcel_audit WHERE event='InputObserved' AND " + predicate + ")", owner, tx);

    // Called once by the strict offline 10/11 -> 12 transaction. No historical
    // shift is inferred; current raw owners are indexed under the explicit legacy scope.
    static void BackfillStationFacts(SqliteConnection connection, SqliteTransaction tx, string application)
    {
        if (application != "sportmaster") return;
        // The projection functions only require db and current shift; a lightweight
        // connection owner avoids constructing/recovering a business repository.
        using var commands = connection.CreateCommand(); commands.Transaction = tx;
        commands.CommandText = "SELECT body FROM parcel_operations UNION ALL SELECT body FROM parcel_archives";
        using (var r = commands.ExecuteReader())
            while (r.Read()) ProjectStationRequestCore(connection, Wire.Decode<ParcelOperation>(r.GetString(0)), tx, "");
        commands.CommandText = "SELECT seq,detail FROM parcel_audit WHERE event='InputObserved' ORDER BY seq";
        using var rows = commands.ExecuteReader();
        while (rows.Read()) ProjectStationObservationCore(connection, Wire.Decode<ParcelInputSnapshot>(rows.GetString(1)), rows.GetInt64(0), tx, "");
    }
}
