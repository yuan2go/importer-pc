using Ipc.Platform;

internal static class StartupCameraDependencies
{
    internal static void Run(string root)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS DEPENDENCIES " + text); }
        var source = Path.Combine(root, "cache"); var release = Path.Combine(root, "release");
        Directory.CreateDirectory(source); Directory.CreateDirectory(release);
        var location = new DeploymentLocation("sportmaster", new string('a', 40), release, Path.Combine(root, "config"), Path.Combine(root, "data"), "synthetic") { MaintenanceRoot = source };
        var names = new[] { "app/vendor/hikrobot-mvs/camera.dll", "desktop/vendor/hikrobot-mvid/reader.dll", "desktop/vendor/zxing-cpp/ZXing.dll", "MVIDCodeReaderConfig.ini" };
        var files = new Dictionary<string, string>();
        foreach (var name in names)
        {
            var path = Path.Combine(source, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "SYNTHETIC dependency " + name); files[name] = Wire.FileHash(path);
        }
        var configuration = Path.Combine(root, "config/business.json"); Directory.CreateDirectory(Path.GetDirectoryName(configuration)!);
        File.WriteAllText(configuration, "original station configuration"); var configHash = Wire.FileHash(configuration);
        Check(WindowsDeploymentSetup.CameraDependencyDifferences(release, files).Length == names.Length, "both SDK routes, decoder and working configuration are detected before business startup");
        Check(WindowsDeploymentSetup.RestoreCameraDependencyFiles(location, files).Length == names.Length &&
            names.All(name => Wire.FileHash(Path.Combine(release, name)) == files[name]), "exact installed bytes are restored from the independent cache");
        Check(WindowsDeploymentSetup.RestoreCameraDependencyFiles(location, files).Length == 0 && Wire.FileHash(configuration) == configHash,
            "healthy subsequent startup makes no copy and preserves the station configuration");
        File.WriteAllText(Path.Combine(release, names[0]), "corrupt");
        Check(WindowsDeploymentSetup.RestoreCameraDependencyFiles(location, files).SequenceEqual([names[0]]), "a corrupt dependency is replaced only by the frozen matching bytes");
        foreach (var name in names.Take(2)) File.Delete(Path.Combine(release, name));
        File.WriteAllText(Path.Combine(source, names[1]), "corrupt recovery source");
        try { WindowsDeploymentSetup.RestoreCameraDependencyFiles(location, files); throw new Exception("tampered source accepted"); }
        catch (InvalidDataException) { }
        Check(names.Take(2).All(name => !File.Exists(Path.Combine(release, name))), "all recovery sources are verified before the first target mutation");
        File.WriteAllText(Path.Combine(source, names[1]), "SYNTHETIC dependency " + names[1]);
        WindowsDeploymentSetup.RestoreCameraDependencyFiles(location, files);
        File.WriteAllText(Path.Combine(release, names[0]), "loaded corrupt file");
        using (var loaded = new FileStream(Path.Combine(release, names[0]), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { WindowsDeploymentSetup.RestoreCameraDependencyFiles(location, files); throw new Exception("loaded dependency overwritten"); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            Check(File.ReadAllText(Path.Combine(release, names[0])) == "loaded corrupt file", "an occupied DLL is not overwritten or reported recovered");
        }
        WindowsDeploymentSetup.RestoreCameraDependencyFiles(location, files);
        Check(!Directory.EnumerateFiles(release, "*.repair", SearchOption.AllDirectories).Any(), "failed and successful recovery leave no temporary file");
        var unsafeFiles = new Dictionary<string, string> { ["desktop/vendor/hikrobot-mvid/../../escape.dll"] = files[names[0]] };
        try { WindowsDeploymentSetup.RestoreCameraDependencyFiles(location, unsafeFiles); throw new Exception("unsafe path accepted"); }
        catch (InvalidDataException) { }
        Check(!File.Exists(Path.Combine(release, "desktop/escape.dll")) && !WindowsDeploymentSetup.IsCameraDependency("app/config/business.json") &&
            !WindowsDeploymentSetup.IsCameraDependency("app/Ipc.Platform.dll"), "recovery rejects traversal and has no configuration, database or general application write scope");
    }
}
