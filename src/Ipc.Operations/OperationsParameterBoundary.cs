using System.Text.Json;
using Ipc.Application.Contracts;

namespace Ipc.Operations;

internal static class OperationsParameterBoundary
{
    internal static void RequireTechnical(ParcelConfiguration original, ParcelConfiguration candidate)
    {
        var allowed = SmParameterOwnership.ProjectCandidate(original, candidate, SmParameterAudience.Technical);
        if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(allowed), JsonSerializer.SerializeToElement(candidate)))
            throw new UnauthorizedAccessException("专业参数不得修改现场身份、连接地址或组件归属；原配置保留。");
    }
}
