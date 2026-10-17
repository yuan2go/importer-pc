using System.Globalization;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

namespace Ipc.Supply;

public sealed partial class SupplyStore
{
    // Application owner serializes this read with writes. The database applies all
    // predicates and paging before JSON materialization, including empty sessions.
    public HistoryPage QueryHistory(HistoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!Enum.IsDefined(query.Entity) ||
            query.SessionStatus is { } sessionStatus && !Enum.IsDefined(sessionStatus) ||
            query.OperationStatus is { } operationStatus && !Enum.IsDefined(operationStatus) ||
            query.RequestStatus is { } requestStatus && !Enum.IsDefined(requestStatus) ||
            query.RequestKind is { } requestKind && !Enum.IsDefined(requestKind))
            throw new ArgumentException("Unknown history query status or entity", nameof(query));
        if (query.Offset < 0 || query.Limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(query), "History offset must be nonnegative and limit 1..200");
        if (query.ThroughRowId is < 0 || query.ThroughRowId is null && query.ThroughId is not null)
            throw new ArgumentException("Invalid historical paging boundary", nameof(query));
        if (query.FromInclusive is { } from && query.UntilExclusive is { } until && from >= until)
            throw new ArgumentException("History start must precede exclusive end", nameof(query));
        var q = query with
        {
            SessionId = HistoryText(query.SessionId), BoxBarcode = HistoryText(query.BoxBarcode),
            ItemBarcode = HistoryText(query.ItemBarcode), Sku = HistoryText(query.Sku),
            TaskId = HistoryText(query.TaskId), Lane = HistoryText(query.Lane)
        };
        var operations = q.Entity == HistoryEntity.Operations;
        var table = operations ? "operations" : "sessions";
        if (q.ThroughRowId is null)
        {
            using var boundary = Sql("SELECT rowid,id FROM " + table + " ORDER BY rowid DESC LIMIT 1", null);
            using var last = boundary.ExecuteReader();
            q = last.Read() ? q with { ThroughRowId = last.GetInt64(0), ThroughId = last.GetString(1) } :
                q with { ThroughRowId = 0, ThroughId = null };
        }
        else if (q.ThroughRowId == 0)
        {
            if (q.ThroughId is not null) throw new ArgumentException("Empty history boundary cannot have an identity", nameof(query));
        }
        else if (string.IsNullOrWhiteSpace(q.ThroughId) ||
            Scalar("SELECT id FROM " + table + " WHERE rowid=$a", q.ThroughRowId) != q.ThroughId)
        {
            // Plain SQLite rowids may be reused after deleting the highest row. Never
            // interpret such a replacement as belonging to the original result set.
            throw new InvalidOperationException("Historical paging boundary was removed or replaced; repeat the query");
        }
        var date = operations ? "json_extract(o.json,'$.CreatedAt')" : "json_extract(s.json,'$.OpenedAt')";
        var id = operations ? "o.id" : "s.id";
        var source = operations ? "operations o JOIN sessions s ON s.id=o.session" : "sessions s";
        var parameters = new Dictionary<string, object?> { ["$through"] = q.ThroughRowId };
        var predicates = new List<string> { (operations ? "o.rowid" : "s.rowid") + "<=$through" };
        void Equal(List<string> target, string field, string name, object? value)
        {
            if (value is null) return;
            parameters.Add(name, value);
            target.Add(field + "=" + name);
        }
        Equal(predicates, "s.id", "$session", q.SessionId);
        Equal(predicates, "json_extract(s.json,'$.Barcode')", "$box", q.BoxBarcode);
        Equal(predicates, "json_extract(s.json,'$.Status')", "$sessionStatus", q.SessionStatus?.ToString());
        if (q.FromInclusive is { } start)
        {
            parameters.Add("$from", start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            predicates.Add("julianday(" + date + ")>=julianday($from)");
        }
        if (q.UntilExclusive is { } end)
        {
            parameters.Add("$until", end.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            predicates.Add("julianday(" + date + ")<julianday($until)");
        }
        var operationPredicates = new List<string>();
        Equal(operationPredicates, "o.lane", "$lane", q.Lane);
        Equal(operationPredicates, "json_extract(o.json,'$.Barcode')", "$item", q.ItemBarcode);
        Equal(operationPredicates, "json_extract(o.json,'$.Task.Sku')", "$sku", q.Sku);
        Equal(operationPredicates, "json_extract(o.json,'$.Task.TaskId')", "$task", q.TaskId);
        Equal(operationPredicates, "json_extract(o.json,'$.Status')", "$operationStatus", q.OperationStatus?.ToString());
        if (operationPredicates.Count > 0)
        {
            if (operations) predicates.AddRange(operationPredicates);
            else predicates.Add("EXISTS (SELECT 1 FROM operations o WHERE o.session=s.id AND " + string.Join(" AND ", operationPredicates) + ")");
        }
        var requestPredicates = new List<string>();
        Equal(requestPredicates, "r.status", "$requestStatus", q.RequestStatus?.ToString());
        Equal(requestPredicates, "r.kind", "$requestKind", q.RequestKind?.ToString());
        if (requestPredicates.Count > 0)
            predicates.Add("EXISTS (SELECT 1 FROM requests r WHERE " +
                (operations ? "r.owner=o.id" : "r.session=s.id") + " AND " + string.Join(" AND ", requestPredicates) + ")");
        var where = predicates.Count == 0 ? "" : " WHERE " + string.Join(" AND ", predicates);
        using var count = HistoryCommand("SELECT count(*) FROM " + source + where, parameters);
        var total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        parameters.Add("$limit", q.Limit); parameters.Add("$offset", q.Offset);
        using var command = HistoryCommand("SELECT s.json," + (operations ? "o.json" : "NULL") + "," + date +
            ",(SELECT count(*) FROM operations oc WHERE oc.session=s.id)," +
            "(SELECT count(*) FROM requests rc WHERE " + (operations ? "rc.owner=o.id" : "rc.session=s.id") + ")," +
            "(SELECT coalesce(sum(a.quantity),0) FROM acceptance a JOIN requests ar ON ar.id=a.request WHERE " +
            (operations ? "a.operation=o.id" : "ar.session=s.id") + ") FROM " + source + where +
            " ORDER BY coalesce(julianday(" + date + "),0) DESC," + id + " ASC LIMIT $limit OFFSET $offset", parameters);
        using var reader = command.ExecuteReader();
        var entries = new List<HistoryEntry>();
        while (reader.Read())
        {
            var session = Wire.Decode<StationSession>(reader.GetString(0));
            var operation = reader.IsDBNull(1) ? null : Wire.Decode<SupplyOperation>(reader.GetString(1));
            entries.Add(new(operation?.Id ?? session.Id, session.Id, session, operation,
                reader.IsDBNull(2) ? null : HistoryDate(reader.GetString(2)), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5)));
        }
        return new(q, entries.ToArray(), total);
    }

    // Session-owned requests contain the Context / CheckClose / Close chain even
    // when no operation was admitted. Operation request chains remain separate.
    public SessionHistoryDetail SessionHistory(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Session identity required", nameof(sessionId));
        var session = Session(sessionId) ?? throw new KeyNotFoundException("Historical session was not found");
        var opened = Scalar("SELECT json_extract(json,'$.OpenedAt') FROM sessions WHERE id=$a", sessionId);
        var requests = Read<BusinessRequest>("SELECT json FROM requests WHERE session=$a AND owner=$a ORDER BY coalesce(julianday(json_extract(json,'$.CreatedAt')),0),id", sessionId);
        var details = requests.Select(request => new RequestDetail(request, Attempts(request.Id), Responses(request.Id),
            Audits(request.OwnerId).Concat(Audits(request.Id)).DistinctBy(a => a.Id).OrderBy(a => a.At).ThenBy(a => a.Id, StringComparer.Ordinal).ToArray()) { Conflicts = Conflicts(request.Id) }).ToArray();
        var operations = int.Parse(Scalar("SELECT count(*) FROM operations WHERE session=$a", sessionId)!, CultureInfo.InvariantCulture);
        return new(session, HistoryDate(opened), details,
            Audits(sessionId).OrderBy(a => a.At).ThenBy(a => a.Id, StringComparer.Ordinal).ToArray(), operations, AcceptedQuantity(sessionId));
    }

    SqliteCommand HistoryCommand(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        var command = db.CreateCommand(); command.CommandText = sql; command.Transaction = current;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
        return command;
    }
    static string? HistoryText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > 512) throw new ArgumentException("History field exceeds 512 characters");
        return value;
    }
    static DateTimeOffset? HistoryDate(string? value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var date) ? date : null;
}
