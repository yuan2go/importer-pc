namespace Ipc.Platform;

// A checked release declaration, never permission to convert data or retry work.
public sealed record DeploymentRuntimeCompatibility(int SchemaVersion, int DataFormat, int ConfigurationSchema,
    int CredentialSchema, int ManagementProtocol, int RecoverySecurityEpoch, int LifecycleProtocol,
    string ApplicationContractVersion, string ProviderPolicySha256, string DependencyInventorySha256)
{
    public const int CurrentLifecycleProtocol = 3;
    public const string FileName = "deployment-compatibility.json";

    public void Validate(ApplicationRelease release)
    {
        if (SchemaVersion != 1 || DataFormat < 1 || ConfigurationSchema < 1 || CredentialSchema != LocalManagementMode.CredentialSchemaVersion ||
            ManagementProtocol != ManagementModeBroker.ProtocolVersion || RecoverySecurityEpoch != release.RecoverySecurityEpoch || LifecycleProtocol != CurrentLifecycleProtocol ||
            !ExtensionSchema.IsVersion(ApplicationContractVersion) || !ExtensionSchema.IsHash(ProviderPolicySha256) ||
            ProviderPolicySha256 != release.CompatibilitySha256 || DependencyInventorySha256 != DependencyHash(release.Files))
            throw new InvalidDataException("程序兼容声明与原库存不一致，保留原件。");
    }

    public static string DependencyHash(IReadOnlyDictionary<string, string> files) => Wire.Hash(string.Join("\n", files
        .Where(p => Path.GetExtension(p.Key).ToLowerInvariant() is ".dll" or ".exe" &&
            !Path.GetFileName(p.Key).StartsWith("Ipc.", StringComparison.OrdinalIgnoreCase))
        .OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "\t" + p.Value.ToUpperInvariant())));

    public static string? Incompatibility(DeploymentRuntimeCompatibility current, DeploymentRuntimeCompatibility target)
    {
        if (target.RecoverySecurityEpoch < RecoverySecurityPolicy.CurrentEpoch) return "上一版本低于当前安全下限。";
        if (current.DataFormat != target.DataFormat || current.ConfigurationSchema != target.ConfigurationSchema)
            return "上一版本不能读取当前数据或配置；程序回退不会还原旧数据库。";
        if (current.CredentialSchema != target.CredentialSchema || current.ManagementProtocol != target.ManagementProtocol ||
            current.LifecycleProtocol != target.LifecycleProtocol || target.LifecycleProtocol != CurrentLifecycleProtocol)
            return "上一版本的管理服务或维护协议不兼容。";
        if (current.ApplicationContractVersion != target.ApplicationContractVersion || current.ProviderPolicySha256 != target.ProviderPolicySha256)
            return "上一版本的业务或设备契约不兼容。";
        if (current.DependencyInventorySha256 != target.DependencyInventorySha256) return "上一版本的SDK或基础运行文件不同，不能直接回退。";
        return null;
    }
}
