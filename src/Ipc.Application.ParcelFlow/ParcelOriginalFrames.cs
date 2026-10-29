using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    readonly SemaphoreSlim originalFrameWriters = new(2, 2);
    static readonly uint[] pngCrc = Enumerable.Range(0, 256).Select(value =>
    {
        var crc = (uint)value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320); return crc;
    }).ToArray();
    void RecoverOriginalFrameArchives()
    {
        foreach (var op in operations.Values.Where(o => o.OriginalFrame?.State == "Pending").ToArray())
            Save(op with { OriginalFrame = op.OriginalFrame! with { State = "Interrupted", At = DateTimeOffset.UtcNow,
                Failure = "Process stopped before archive completion; preserve any original file; never substitute a new frame or replay the business request" } },
                "OriginalFrameInterrupted", "Image archive recovery only; original business facts unchanged");
        foreach (var op in operations.Values.Where(o => o.OriginalFrame?.State == "Saved").ToArray())
            try
            {
                var original = op.OriginalFrame!;
                if (original.Acquisition != op.Acquisition || original.RelativeFile != "images/original-frames/" + op.Id + ".png" ||
                    !ProductValidation.IsSafeRelativeFile(directory!, original.RelativeFile, out var file) ||
                    !ExtensionSchema.IsHash(original.FileSha256 ?? "") || new FileInfo(file).Length != original.Length || Wire.FileHash(file) != original.FileSha256)
                    throw new InvalidDataException("Archived original image identity/length/hash differs");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Save(op with { OriginalFrame = op.OriginalFrame! with { State = "Failed", At = DateTimeOffset.UtcNow, Failure = error.GetType().Name + ": " + error.Message } },
                    "OriginalFrameIntegrityFailed", "Original business state and image evidence hashes retained; no new photo/request");
            }
    }
    void ScheduleOriginalFrame(ParcelOperation op, IOriginalFrameLease pixels)
    {
        auxiliary.Add(Task.Run(async () =>
        {
            activeIngress.Value = null;
            OriginalFrameArchive result;
            await originalFrameWriters.WaitAsync().ConfigureAwait(false);
            try
            {
                using (pixels)
                {
                    Boundary?.Invoke("BeforeOriginalFrameWrite");
                    result = SaveOriginalFrameFile(directory!, op.OriginalFrame!, pixels, op.FrozenConfiguration.Retention?.MinimumFreeBytes ?? 67108864);
                    Boundary?.Invoke("AfterOriginalFrameWrite");
                }
            }
            catch (Exception error)
            {
                pixels.Dispose();
                result = op.OriginalFrame! with { State = "Failed", At = DateTimeOffset.UtcNow, Failure = error.GetType().Name + ": " + error.Message };
            }
            finally { originalFrameWriters.Release(); }
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                // Read current state after IO. Never overwrite a later WCS fact,
                // frozen request, attempt or Unknown with the scheduling snapshot.
                var current = operations[op.Id];
                Save(current with { OriginalFrame = result }, "OriginalFrame" + result.State, "Same acquisition raw pixels; auxiliary archive only; original requests unchanged");
                if (result.State != "Saved") Diagnostic("原图留档", "保存识码原图失败", "错误", "Operation=" + op.Id + "; " + result.Failure);
            }
            finally { gate.Release(); }
        }));
    }
    internal static OriginalFrameArchive SaveOriginalFrameFile(string directory, OriginalFrameArchive frame, IOriginalFrameLease pixels, long reserve)
    {
        if (frame.State != "Pending" || pixels.Width != frame.Width || pixels.Height != frame.Height ||
            pixels.Pixels.Length != checked(frame.Width * frame.Height) || pixels.Pixels.Length > 33554432 ||
            !frame.RelativeFile.StartsWith("images/original-frames/", StringComparison.Ordinal) ||
            !ProductValidation.IsSafeRelativeFile(directory, frame.RelativeFile, out var destination))
            throw new InvalidDataException("Original frame identity/layout/path invalid");
        RecoveryFiles.RequireOrdinaryAncestors(destination);
        if (!ParcelStorageVolume.Plan(directory, checked(pixels.Pixels.Length * 2L + 4096), reserve).Fits)
            throw new IOException("Original frame archive cannot consume reserved storage");
        var rawHash = Convert.ToHexString(SHA256.HashData(pixels.Pixels.Span));
        var encoded = EncodeOriginalPng(pixels.Pixels.Span, frame.Width, frame.Height);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".pending";
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { file.Write(encoded); file.Flush(true); }
        File.Move(temporary, destination); // never overwrite another acquisition or an uncertain previous file
        return frame with { State = "Saved", At = DateTimeOffset.UtcNow, RawSha256 = rawHash,
            FileSha256 = Convert.ToHexString(SHA256.HashData(encoded)), Length = encoded.Length };
    }
    internal static byte[] EncodeOriginalPng(ReadOnlySpan<byte> pixels, int width, int height)
    {
        if (width <= 0 || height <= 0 || pixels.Length != checked(width * height)) throw new InvalidDataException("Tight Mono8 pixels required");
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            for (var row = 0; row < height; row++) { zlib.WriteByte(0); zlib.Write(pixels.Slice(row * width, width)); }
        using var png = new MemoryStream(); png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        static void Chunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            Span<byte> word = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(word, (uint)data.Length); output.Write(word); output.Write(type); output.Write(data);
            uint crc = 0xffffffff;
            foreach (var value in type) crc = (crc >> 8) ^ pngCrc[(crc ^ value) & 255];
            foreach (var value in data) crc = (crc >> 8) ^ pngCrc[(crc ^ value) & 255];
            BinaryPrimitives.WriteUInt32BigEndian(word, ~crc); output.Write(word);
        }
        Span<byte> header = stackalloc byte[13]; header.Clear(); BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header[4..], height); header[8] = 8;
        Chunk(png, "IHDR"u8, header); Chunk(png, "IDAT"u8, compressed.ToArray()); Chunk(png, "IEND"u8, []);
        return png.ToArray();
    }
}
