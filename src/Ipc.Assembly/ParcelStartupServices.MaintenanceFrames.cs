using System.Text;
using System.Text.Json;
using Ipc.Platform;

namespace Ipc.Assembly;

public static partial class ParcelStartupServices
{
    internal sealed record MaintenancePipeReply(string? Terminal, bool CancelAccepted);
    static async Task<string?> ReadMaintenanceLine(TextReader input, CancellationToken token, int maximum = 65536)
    {
        var line = new StringBuilder(); var character = new char[1];
        while (await input.ReadAsync(character.AsMemory(), token) != 0)
        {
            if (character[0] == '\n') return line.ToString();
            if (line.Length == maximum) throw new IOException("维护帧超出预算");
            line.Append(character[0]);
        }
        if (line.Length != 0) throw new IOException("维护帧被截断");
        return null;
    }
    static void RequireMaintenanceEnvelope(JsonElement message, string hash)
    {
        if (message.ValueKind != JsonValueKind.Object || message.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != message.EnumerateObject().Count() ||
            message.GetProperty("RequestSha256").GetString() != hash)
            throw new InvalidDataException("维护帧身份不匹配");
    }
    internal static async Task<MaintenancePipeReply> ReadBoundMaintenanceResponse(TextReader input, string hash, bool requireReady,
        Action<WindowsInstallationStage> installation, Action<MaintenanceProgress> maintenance, Func<Task> ready, CancellationToken token)
    {
        var events = 0; var seenReady = false; var accepted = false;
        while (await ReadMaintenanceLine(input, token) is { } line)
        {
            if (++events > 2048) throw new IOException("维护帧总量超出预算");
            using var document = JsonDocument.Parse(line); var frame = document.RootElement; RequireMaintenanceEnvelope(frame, hash);
            var status = frame.GetProperty("Status").GetString();
            if (status == "Ready")
            {
                if (!requireReady || seenReady) throw new IOException("维护Ready顺序无效");
                seenReady = true; await ready(); continue;
            }
            if (status == "Progress")
            {
                if (requireReady && !seenReady) throw new IOException("维护进度先于Ready");
                if (!Enum.TryParse<WindowsInstallationStage>(frame.GetProperty("Stage").GetString(), out var stage) || !Enum.IsDefined(stage)) throw new IOException("维护安装阶段无效");
                installation(stage); continue;
            }
            if (status == "MaintenanceProgress")
            {
                if (!seenReady || !Enum.TryParse<MaintenanceStage>(frame.GetProperty("Stage").GetString(), out var stage) || !Enum.IsDefined(stage)) throw new IOException("维护阶段无效");
                long? ReadCount(string name) => frame.GetProperty(name).ValueKind == JsonValueKind.Null ? null : frame.GetProperty(name).GetInt64();
                var done = ReadCount("ProcessedBytes"); var total = ReadCount("TotalBytes");
                if (done < 0 || total < 0 || done > total) throw new IOException("维护处理量无效");
                maintenance(new(stage, done, total, frame.GetProperty("CanCancel").GetBoolean())); continue;
            }
            if (status == "CancelAck")
            {
                if (!seenReady) throw new IOException("取消回执先于Ready");
                var value = frame.GetProperty("Decision").GetString(); if (value is not ("Accepted" or "TooLate")) throw new IOException("取消回执无效");
                accepted |= value == "Accepted"; maintenance(new(value == "Accepted" ? MaintenanceStage.CancelAccepted : MaintenanceStage.CancelTooLate, null, null, false)); continue;
            }
            if (status is not ("Completed" or "Failed" or "Unknown" or "Cancelled")) throw new IOException("未知维护终态");
            if (status is "Failed" or "Unknown" && (!frame.TryGetProperty("Error", out var error) || error.ValueKind != JsonValueKind.String)) throw new IOException("维护失败回执缺少原因");
            if (requireReady && !seenReady && status is "Completed" or "Cancelled") throw new IOException("维护终态缺少开始决定");
            return new(line, accepted);
        }
        return new(null, accepted);
    }
    internal static string? CompleteBoundMaintenanceResponse(MaintenancePipeReply reply, int exitCode, bool cancellationRequested)
    {
        if (reply.Terminal is not null)
        {
            using var document = JsonDocument.Parse(reply.Terminal);
            var status = document.RootElement.GetProperty("Status").GetString();
            if (reply.CancelAccepted && status == "Completed") throw new InstallationOutcomeUnknownException("已接受取消却报告完成，保留原件核对。");
            if (status == "Cancelled")
            {
                if (exitCode != 3 || !reply.CancelAccepted || !cancellationRequested) throw new InstallationOutcomeUnknownException("取消未形成可信终态，请核对原记录。");
                throw new MaintenanceCancelledException("维护已在提交前取消；已生成的备份、未完成副本或隔离目录保留，请核对后重新选择目标或计划。");
            }
        }
        return CompleteMaintenanceResponse(reply.Terminal, exitCode);
    }
    internal static string? CompleteMaintenanceResponse(string? response, int exitCode)
    {
        string? failure = null, result = null;
        try
        {
            if (response is null || response.Length > 65536) throw new IOException("维护终态回执缺失");
            using var document = System.Text.Json.JsonDocument.Parse(response);
            var terminal = document.RootElement.GetProperty("Status").GetString();
            if (terminal is not ("Completed" or "Failed") || terminal == "Completed" && exitCode != 0 || terminal == "Failed" && exitCode != 2) throw new IOException("维护终态与进程结果不一致");
            if (terminal == "Failed") failure = document.RootElement.GetProperty("Error").GetString() ?? "维护未完成";
            if (document.RootElement.TryGetProperty("Result", out var value))
            {
                if (value.ValueKind is not (System.Text.Json.JsonValueKind.String or System.Text.Json.JsonValueKind.Null)) throw new IOException("维护终态结果格式无效");
                result = value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
            }
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new InstallationOutcomeUnknownException("未取得可信维护终态，请先核对安装记录。", error); }
        if (failure is not null) throw new IOException(failure);
        return result;
    }
}
