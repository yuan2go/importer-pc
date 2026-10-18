namespace Ipc.Platform;

public sealed record ApplicationRelease(int SchemaVersion, string ApplicationId, string SourceCommit,
    string[] Views, string[] Layouts, Dictionary<string, string> Files);
public static class ApplicationReleaseValidation
{
    // Hashes establish installed-byte integrity, not publisher trust or field qualification.
    // Trusted distribution/ACLs remain deployment responsibilities.
    public static void ValidateIfPublished(string directory, string application, BusinessPresentation presentation)
    {
        var file = Path.Combine(directory, "application-release.json");
        if (!File.Exists(file)) return; // Explicit source/development build; Production is independently gated.
        var release = Wire.Decode<ApplicationRelease>(File.ReadAllText(file));
        if (release.SchemaVersion != 1 || release.ApplicationId != application || release.SourceCommit.Length != 40 || !release.SourceCommit.All(char.IsAsciiHexDigit) ||
            !release.Views.SequenceEqual(presentation.Views) || !release.Layouts.SequenceEqual(presentation.Layouts) || release.Files.Count == 0)
            throw new InvalidDataException("Installed Application registration does not match this host/presentation");
        foreach (var entry in release.Files)
            if (!ProductValidation.IsSafeRelativeFile(directory, entry.Key, out var path) || !ExtensionSchema.IsHash(entry.Value) || Wire.FileHash(path) != entry.Value)
                throw new InvalidDataException("Installed Application byte integrity failed: " + entry.Key);
    }
}
