using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Ipc.Application.ParcelFlow;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

internal static class PresenceSampleReplay
{
    internal sealed record Plan(int Schema, string SourceCommit, string SourceKind, string DeviceId, string ReaderEngine, string ClockBasis, Frame[] Frames);
    internal sealed record Frame(string SessionId, uint FrameNumber, double HostElapsedMs, string RelativeFile, string Sha256,
        int Width, int Height, bool ConfirmEmptyCalibration, TrayPresence? OperatorExpected);
    internal static void Run(string manifest, string output)
    {
        manifest = Path.GetFullPath(manifest); output = Path.GetFullPath(output);
        RecoveryFiles.RequireOrdinaryAncestors(manifest); RecoveryFiles.RequireOrdinaryAncestors(output);
        if (new FileInfo(manifest).Length > 4194304 || File.Exists(output) || Directory.Exists(output)) throw new IOException("Use a bounded manifest and a new output file");
        var root = Path.GetDirectoryName(manifest)!;
        var relativeOutput = Path.GetRelativePath(root, output);
        if (!Path.IsPathRooted(relativeOutput) && relativeOutput != ".." && !relativeOutput.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Replay output must be outside original sample custody");
        var manifestBytes = ReadBounded(manifest, 4194304);
        var manifestHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(manifestBytes));
        var plan = Wire.Decode<Plan>(System.Text.Encoding.UTF8.GetString(manifestBytes));
        if (plan.Schema != 1 || !Regex.IsMatch(plan.SourceCommit, "^[a-f0-9]{40}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ||
            plan.SourceKind is not ("FieldCaptured" or "Synthetic") || !ExtensionSchema.IsId(plan.DeviceId) ||
            plan.ReaderEngine is not ("mvid-native" or "zxing-cpp") || plan.Frames is null || plan.Frames.Length is < 1 or > 10000)
            throw new InvalidDataException("Explicit sample source, device, route and bounded frames are required");
        if (plan.ClockBasis is not ("HostMonotonicVerified" or "CaptureWallClockOnly" or "Synthetic"))
            throw new InvalidDataException("Declare the actual capture clock evidence; SDK timestamp units are not inferred");
        var watch = Stopwatch.StartNew(); long bytes = 0; var model = new MvsTrayPresence();
        var sessions = new HashSet<string>(); string? session = null; uint previous = 0; double elapsed = -1;
        var evidence = new List<object>(); int falseEmpty = 0, unknown = 0, unlabeled = 0;
        foreach (var frame in plan.Frames)
        {
            if (watch.Elapsed > TimeSpan.FromMinutes(5)) throw new TimeoutException("Presence replay budget expired; original samples retained");
            if (!ExtensionSchema.IsId(frame.SessionId) || frame.FrameNumber == 0 || !double.IsFinite(frame.HostElapsedMs) || frame.HostElapsedMs < 0 || frame.HostElapsedMs > 604800000 ||
                !ExtensionSchema.IsHash(frame.Sha256) || frame.Width <= 0 || frame.Height <= 0 || (long)frame.Width * frame.Height > 16777216 ||
                frame.OperatorExpected is { } expected && !Enum.IsDefined(expected)) throw new InvalidDataException("Frame identity/clock/shape is invalid");
            if (session != frame.SessionId)
            {
                if (!sessions.Add(frame.SessionId)) throw new InvalidDataException("A prior camera session cannot re-enter a newer session");
                model = new(); session = frame.SessionId; previous = 0; elapsed = -1;
            }
            if (frame.FrameNumber <= previous || frame.HostElapsedMs <= elapsed) throw new InvalidDataException("Frame/session chronology differs");
            if (previous != 0 && frame.FrameNumber != previous + 1) model.BreakContinuity();
            previous = frame.FrameNumber; elapsed = frame.HostElapsedMs;
            var file = RecoveryFiles.Resolve(root, frame.RelativeFile); RecoveryFiles.RequireOrdinaryAncestors(file);
            var original = ReadBounded(file, 67108864); bytes = checked(bytes + original.Length);
            if (bytes > 2147483648) throw new InvalidDataException("Captured sample set exceeds replay quota");
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)) != frame.Sha256) throw new InvalidDataException("Frame changed during replay read");
            byte[] pixels;
            if (file.EndsWith(".pgm", StringComparison.OrdinalIgnoreCase))
            {
                // Exact lossless Mono8 layout emitted by CameraInspectionStore.ExportAsync.
                var header = System.Text.Encoding.ASCII.GetBytes($"P5\n{frame.Width} {frame.Height}\n255\n");
                if (original.Length != header.Length + (long)frame.Width * frame.Height || !original.AsSpan(0, header.Length).SequenceEqual(header))
                    throw new InvalidDataException("Captured Mono8 PGM shape differs");
                pixels = original.AsSpan(header.Length).ToArray();
            }
            else if (file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) pixels = SportmasterRcs.DecodeOriginalPng(original, frame.Width, frame.Height);
            else throw new InvalidDataException("Use original exported Mono8 PGM or parcel PNG; no image conversion is inferred");
            if (frame.ConfirmEmptyCalibration)
            {
                if (frame.OperatorExpected != TrayPresence.Absent) throw new InvalidDataException("Empty calibration requires an explicit operator empty label");
                model.Teach();
            }
            var memory = Marshal.AllocHGlobal(pixels.Length); TrayPresenceEvidence? emitted;
            try
            {
                Marshal.Copy(pixels, 0, memory, pixels.Length);
                emitted = model.Observe(memory, frame.Width, frame.Height, 1 + (long)(frame.HostElapsedMs / 1000 * Stopwatch.Frequency));
            }
            finally { Marshal.FreeHGlobal(memory); }
            var snapshot = model.Snapshot();
            if (snapshot.Presence == TrayPresence.Unknown) unknown++;
            if (frame.OperatorExpected is null) unlabeled++;
            if (frame.OperatorExpected == TrayPresence.Present && snapshot.Presence == TrayPresence.Absent) falseEmpty++;
            evidence.Add(new
            {
                frame.SessionId,
                frame.FrameNumber,
                frame.HostElapsedMs,
                frame.RelativeFile,
                frame.Sha256,
                frame.OperatorExpected,
                EmittedPresence = emitted?.State,
                Model = snapshot
            });
        }
        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ReadBounded(manifest, 4194304))) != manifestHash) throw new InvalidDataException("Manifest changed during replay");
        var result = Wire.Encode(new
        {
            Status = "SAMPLE_REPLAY_REVIEW_REQUIRED",
            plan.SourceCommit,
            plan.SourceKind,
            plan.DeviceId,
            plan.ReaderEngine,
            plan.ClockBasis,
            ManifestSha256 = manifestHash,
            ReplaySourceIdentity = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(MvsTrayPresence).Assembly)?.InformationalVersion,
            Frames = evidence,
            UnknownFrames = unknown,
            UnlabeledFrames = unlabeled,
            FalseEmptyAgainstDeclaredLabels = falseEmpty,
            Scope = "Production Mono8 model only; no SDK/camera/RCS; labels require independent verification; no automatic or 24/72-hour field qualification"
        });
        var staged = output + "." + Wire.Id() + ".new";
        using (var target = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { var encoded = System.Text.Encoding.UTF8.GetBytes(result); target.Write(encoded); target.Flush(true); }
        File.Move(staged, output, overwrite: false);
    }
    static byte[] ReadBounded(string path, int maximum)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (source.Length > maximum) throw new InvalidDataException("Captured original exceeds replay read quota");
        var bytes = new byte[checked((int)source.Length)]; source.ReadExactly(bytes);
        if (source.ReadByte() != -1) throw new InvalidDataException("Captured original changed during bounded read");
        return bytes;
    }
}
