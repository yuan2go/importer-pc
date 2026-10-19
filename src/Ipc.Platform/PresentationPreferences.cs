namespace Ipc.Platform;

// Per-user display choices; never a deployment/configuration/business authority.
public static class PresentationPreferences
{
    sealed record Preference(int Schema, bool Compact);
    static string FileFor(string application, string surface)
    {
        if (!ExtensionSchema.IsId(application) || !ExtensionSchema.IsId(surface)) throw new InvalidDataException("Invalid presentation identity");
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc", "presentation", application + "." + surface + ".json");
        DeploymentPaths.RequireOrdinaryAncestors(path); return path;
    }
    public static bool? Read(string application, string surface)
    {
        var file = FileFor(application, surface); if (!File.Exists(file)) return null;
        if (new FileInfo(file).Length > 4096) throw new InvalidDataException("展示偏好超出预算");
        var preference = Wire.Decode<Preference>(File.ReadAllText(file));
        if (preference.Schema != 1) throw new InvalidDataException("展示偏好版本不支持；保留原文件");
        return preference.Compact;
    }
    public static void Save(string application, string surface, bool compact)
    {
        var file = FileFor(application, surface); _ = Read(application, surface);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!); ExtensionStore.AtomicWrite(file, new Preference(1, compact));
    }
}
