using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

internal static class CameraOriginalExport
{
    internal static CameraInspectionExport Write(CameraOriginalImage image, string destination, long reserve, CancellationToken token)
    {
        var full = Path.GetFullPath(destination);
        if (!full.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("原图导出必须为D盘PNG文件。");
        RecoveryFiles.RequireOrdinaryAncestors(full);
        RecoveryFiles.RequireOrdinaryAncestors(full + ".json");
        if (new DriveInfo(Path.GetPathRoot(full)!).AvailableFreeSpace < Math.Max(512L * 1024 * 1024, reserve) + image.Gray8.LongLength * 2)
            throw new IOException("原图导出空间不足；保留业务存储余量，不创建导出件。");
        if (File.Exists(full) || File.Exists(full + ".json")) throw new IOException("目标已存在，请选择新的文件名。");
        var temporary = full + "." + Wire.Id() + ".partial"; var metadata = full + ".json";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
                var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, image.Metadata.Width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Metadata.Height); header[8] = 8;
                Chunk(file, "IHDR"u8, header);
                using (var compressed = new MemoryStream())
                {
                    using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                        for (var y = 0; y < image.Metadata.Height; y++)
                        { token.ThrowIfCancellationRequested(); zlib.WriteByte(0); zlib.Write(image.Gray8, y * image.Metadata.Width, image.Metadata.Width); }
                    var bytes = compressed.GetBuffer(); var remaining = checked((int)compressed.Length);
                    for (int offset = 0; offset < remaining; offset += 65536)
                    { token.ThrowIfCancellationRequested(); Chunk(file, "IDAT"u8, bytes.AsSpan(offset, Math.Min(65536, remaining - offset))); }
                }
                Chunk(file, "IEND"u8, []); file.Flush(true);
            }
            var hash = Wire.FileHash(temporary); var size = new FileInfo(temporary).Length; token.ThrowIfCancellationRequested();
            // Publish the provenance first. Its explicit PendingImage state means a
            // crash between the two names cannot claim a complete PNG export.
            ExtensionStore.AtomicWrite(metadata, new { State = "PendingImage", Original = image.Metadata, PngSha256 = hash, Bytes = size });
            File.Move(temporary, full, overwrite: false);
            ExtensionStore.AtomicWrite(metadata, new { State = "Complete", Original = image.Metadata, PngSha256 = hash, Bytes = size });
            return new(full, hash, size, 1, 0);
        }
        catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
    }
    static void Chunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> value = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(value, data.Length); stream.Write(value); stream.Write(type); stream.Write(data);
        uint crc = uint.MaxValue;
        foreach (var b in type) { crc ^= b; for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1)); }
        foreach (var b in data) { crc ^= b; for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1)); }
        BinaryPrimitives.WriteUInt32BigEndian(value, ~crc); stream.Write(value);
    }
}
