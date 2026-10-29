using Ipc.Drivers.Hikrobot.Mvs;
internal static partial class MvsVerification
{
    static async Task VerifyPreview()
    {
        var cache = new MvsPreview(); var pixels = Enumerable.Repeat((byte)91, 1280 * 1024).ToArray();
        cache.Offer(pixels, 1280, 1024, "camera", "epoch-1", 1, DateTimeOffset.UtcNow);
        Check(cache.Read() is null, "preview allocates no image until explicitly requested");
        cache.Offer(pixels, 1280, 1024, "camera", "epoch-1", 2, DateTimeOffset.UtcNow);
        var first = cache.Read()!;
        Check(first.Width == 600 && first.Height == 480 && first.Gray8.Length == 288000 && first.Epoch == "epoch-1", "preview is aspect-preserving and bounded to 640x480 with original acquisition identity");
        first.Gray8[0] = 0; pixels[0] = 0;
        Check(cache.Read()!.Gray8[0] == 91, "published image owns its bytes and each reader receives an isolated copy");
        cache.Offer(pixels, 1280, 1024, "camera", "epoch-1", 3, DateTimeOffset.UtcNow);
        Check(cache.Read()!.FrameNumber == 2, "fast acquisition cannot publish more than two thumbnails per second");
        await Task.Delay(2100);
        cache.Offer(pixels, 1280, 1024, "camera", "epoch-1", 4, DateTimeOffset.UtcNow);
        Check(cache.Read() is null, "closing page expires image demand and releases the cached thumbnail");
        cache.Offer(pixels, 1280, 1024, "camera", "epoch-1", 5, DateTimeOffset.UtcNow); cache.Clear();
        Check(cache.Read() is null, "stop or reconnect clears the previous session image");
    }
}
