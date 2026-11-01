using Ipc.Drivers.Hikrobot.Mvs;

internal static partial class MvsVerification
{
    internal static void VerifyPreviewPool()
    {
        var preview = new MvsPreview();
        const int width = 3072, height = 2048;
        var pixels = Enumerable.Repeat((byte)17, width * height).ToArray();
        Check(preview.ReadLease() is null, "preview demand alone never fabricates a frame");
        preview.Offer(pixels, width, height, "pool", "epoch", 1, DateTimeOffset.UtcNow);
        using var first = preview.ReadLease() ?? throw new InvalidOperationException("Missing pooled thumbnail");
        Check(first.Frame.Width <= 640 && first.Frame.Height <= 480 && first.Frame.Width * first.Frame.Height < pixels.Length &&
            first.Frame.Gray8.AsSpan(0, first.Frame.Width * first.Frame.Height).IndexOfAnyExcept((byte)17) < 0,
            "6MP input becomes an identity-preserving bounded thumbnail before any UI handoff");
        Array.Fill(pixels, (byte)77); Thread.Sleep(80);
        preview.Offer(pixels, width, height, "pool", "epoch", 2, DateTimeOffset.UtcNow);
        using var second = preview.ReadLease() ?? throw new InvalidOperationException("Missing next thumbnail");
        Check(first.Frame.Gray8[0] == 17 && second.Frame.Gray8[0] == 77 && second.Frame.FrameNumber == 2,
            "a newer pooled frame never overwrites a consumer's outstanding lease");
        var held = Enumerable.Range(0, 6).Select(_ => preview.ReadLease()!).ToArray();
        try
        {
            var limited = false; try { using var excess = preview.ReadLease(); } catch (InvalidOperationException) { limited = true; }
            Check(limited, "eight outstanding thumbnail leases bound pool ownership even when a consumer forgets to return frames");
        }
        finally { foreach (var lease in held) lease.Dispose(); }
        for (var i = 0; i < 16; i++) { using var warm = preview.ReadLease(); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++) { using var borrowed = preview.ReadLease(); }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated < 2000L * 4096, "2000 warmed preview leases allocate bounded metadata rather than a new large pixel array per frame");
        Console.WriteLine("MEASURE pooled preview leases=2000; allocated_bytes=" + allocated + "; thumbnail_bytes=" + second.Frame.Width * second.Frame.Height + "; actual pool reuse, not field GC qualification");
        preview.Clear();
        Check(second.Frame.Gray8[0] == 77 && preview.ReadLease() is null, "source clear revokes its latest thumbnail while outstanding consumer ownership remains valid");
        second.Dispose(); second.Dispose();
        var disposed = false; try { _ = second.Frame; } catch (ObjectDisposedException) { disposed = true; }
        Check(disposed, "lease release is idempotent and its public frame cannot be read after return");
        preview.Clear();
    }
}
