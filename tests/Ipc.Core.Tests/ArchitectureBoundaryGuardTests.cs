using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class ArchitectureBoundaryGuardTests : CoreRegression
{
    static readonly string[] SharedProjects =
    [
        "Ipc.Platform", "Ipc.Operations", "Ipc.Assembly", "Ipc.Desktop", "Ipc.Application.ParcelFlow",
        "Ipc.Application.ParcelFlow.Desktop", "Ipc.Integrations.SportmasterRcs"
    ];

    [TestMethod]
    public void SharedSourceContainsOnlyTheRegisteredSportmasterLiterals()
    {
        var root = RepositoryRoot();
        var expected = new[]
        {
            """src/Ipc.Application.ParcelFlow.Desktop/BusinessDesktopProfile.cs|internal static BusinessDesktopProfile Station { get; } = new("sportmaster", "SM 供包业务")""",
            """src/Ipc.Application.ParcelFlow/ParcelStorageFormat.cs|internal const string LegacyStationStorageId = "sportmaster";""",
            """src/Ipc.Assembly/ParcelAssembly.cs|"sportmaster",""",
            """src/Ipc.Desktop/ParcelApp.xaml.cs|const string ApplicationId = "sportmaster";""",
            """src/Ipc.Platform/ProductDeploymentProfile.cs|internal static ProductDeploymentProfile IsolatedWorkstation { get; } = new("sportmaster")"""
        };
        var found = new List<string>();
        foreach (var project in SharedProjects)
        {
            var projectDirectory = Path.Combine(root, "src", project);
            foreach (var file in Sources(projectDirectory))
            {
                var path = Path.GetRelativePath(root, file).Replace('\\', '/');
                foreach (var line in File.ReadLines(file))
                    foreach (Match _ in Regex.Matches(line, "\"sportmaster\""))
                        found.Add(path + "|" + line.Trim());
            }
        }
        CollectionAssert.AreEquivalent(expected, found.ToArray());
    }

    static IEnumerable<string> Sources(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory))
            if (Path.GetExtension(path) is ".cs" or ".xaml" or ".csproj" or ".json" or ".props" or ".targets" or ".resx")
                yield return path;
        foreach (var child in Directory.EnumerateDirectories(directory))
            if (Path.GetFileName(child) is not ("bin" or "obj" or ".artifacts") &&
                !File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
                foreach (var path in Sources(child)) yield return path;
    }
}
