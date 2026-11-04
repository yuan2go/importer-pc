using System.Runtime.InteropServices;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;
using ZXingCpp;

// Tilted labels at site resolution, shaped like the SM site parcels: a small white label with short Code128 bars and a
// human-readable line under them, on a glossy bag with a large dark print and highlights, on a speckled floor.
// Synthetic codes only. Checks that every angle reads once, that the deskewed search stops as soon as the label is
// explained, and that a second code (stacked, end to end, or the same text twice) is still found.
internal static class TiltedDecode
{
    const int Width = 3072, Height = 2048;
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("PASS TILT " + message); }

    internal static void Run()
    {
        var cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? throw new InvalidOperationException("D-drive restored cache required");
        var library = NativeLibrary.Load(Path.Combine(cache, OperatingSystem.IsWindows() ? "zxingcpp/0.5.3/runtimes/win-x64/native/ZXing.dll" : "zxingcpp/0.5.3/runtimes/linux-x64/native/libZXing.so"));
        NativeLibrary.SetDllImportResolver(typeof(BarcodeReader).Assembly, (name, _, _) => name is "ZXing" or "ZXing.dll" ? library : 0);
        var decoder = new SoftwareDecoder("utf-8", () => { }, settings: new ReaderSettings(["Code128", "Code39", "EAN13"], true));
        decoder.Prepare();
        try
        {
            RecognitionSymbol[] Decode(byte[] frame, out long full, out long early)
            {
                decoder.Prepare(); // fresh stage counters for this frame
                var found = decoder.DecodeDetailed(frame, frame.Length, Width, Height);
                var stages = decoder.TimingSnapshot.Stages;
                long Count(string key) => stages.TryGetValue(key, out var s) ? s.Count : 0;
                full = Count("regions.full-search");
                early = Count("regions.explained-before-search") + Count("regions.explained-early") + Count("regions.explained-sharpened");
                return found;
            }

            var angles = new[] { 0, 15, 30, 45, 60, 75, 120, 150 };
            var single = angles.Select(angle => (angle, found: Decode(Frame(angle, "SYNTH-TILT-01"), out var full, out var early), full, early)).ToArray();
            foreach (var (angle, found, _, _) in single.Where(r => r.found is not [{ Text: "SYNTH-TILT-01" }]))
                Console.WriteLine($"DIAGNOSTIC angle={angle} candidates={Wire.Encode(found.Select(f => f.Text))}");
            Check(single.All(r => r.found is [{ Text: "SYNTH-TILT-01" }]),
                "a site-style short-bar label tilted 0-150 degrees on a glossy bag reads as exactly one candidate");
            Check(single.All(r => r.full == 0 && r.early > 0),
                "every angle stops the deskewed search once the label is explained: " + string.Join(", ", single.Select(r => $"{r.angle}°:{r.early}/{r.full}")));

            foreach (var angle in new[] { 30, 60 })
            {
                var stacked = Decode(Frame(angle, "SYNTH-TILT-01", "SYNTH-TILT-02"), out _, out _);
                Check(stacked.Select(s => s.Text).Order().SequenceEqual(new[] { "SYNTH-TILT-01", "SYNTH-TILT-02" }),
                    $"two different codes stacked one label row apart at {angle} degrees are both found, so the frame is rejected as multiple codes");
            }
            var endToEnd = Decode(EndToEnd(30), out _, out _);
            Check(endToEnd.Select(s => s.Text).Order().SequenceEqual(new[] { "SYNTH-TILT-01", "SYNTH-TILT-03" }),
                "two codes printed end to end at 30 degrees are both found");
            foreach (var angle in new[] { 0, 30, 45, 60 })
            {
                // One search can report a box spanning both rows; it is split into its printed bands, and a box that is
                // not one band never ends the search early.
                var twice = Decode(Frame(angle, "SYNTH-TILT-01", "SYNTH-TILT-01"), out _, out _);
                Check(twice.Length >= 2 && twice.All(s => s.Text == "SYNTH-TILT-01"),
                    $"the same code printed twice on a label tilted {angle} degrees stays at least two physical candidates");
            }
        }
        finally { decoder.Release(); }
    }

    static byte[] EndToEnd(double degrees)
    {
        var first = Bars("SYNTH-TILT-01"); var second = Bars("SYNTH-TILT-03");
        // A 20-module gap (scale 3) between the two symbols' quiet zones, the minimum two codes keep.
        var row = new byte[first.Length + 60 + second.Length]; Array.Fill(row, (byte)230);
        first.CopyTo(row, 0); second.CopyTo(row, first.Length + 60);
        return Compose(degrees, [row], row.Length);
    }

    // One label row per text, top to bottom. Each row is a short-bar code with a human-readable line under it.
    static byte[] Frame(double degrees, params string[] texts)
    {
        var bars = texts.Select(Bars).ToArray();
        var width = bars.Max(b => b.Length);
        return Compose(degrees, bars, width);
    }

    const int Scale = 3, BarHeight = 64, TextHeight = 24, RowGap = 36, Margin = 40;
    static byte[] Bars(string text)
    {
        using var barcode = new Barcode(text, new CreatorOptions(BarcodeFormat.Code128));
        using var image = barcode.ToImage(new WriterOptions { Scale = Scale, AddQuietZones = true });
        var row = new byte[image.Width]; Marshal.Copy(image.Data, row, 0, row.Length);
        return row.Select(v => v < 128 ? (byte)35 : (byte)230).ToArray();
    }

    static byte[] Compose(double degrees, byte[][] rows, int barsWidth)
    {
        // White label paper holding every row: bars, then a line of glyph-like marks, then the gap to the next row.
        int lw = barsWidth + 2 * Margin, rowHeight = BarHeight + 8 + TextHeight + RowGap, lh = rows.Length * rowHeight + 2 * Margin - RowGap;
        var label = new byte[lw * lh]; Array.Fill(label, (byte)230);
        var glyphs = new Random(7);
        for (var r = 0; r < rows.Length; r++)
        {
            var top = Margin + r * rowHeight;
            for (var y = 0; y < BarHeight; y++) rows[r].CopyTo(label, (top + y) * lw + Margin);
            for (var x = Margin + barsWidth / 4; x < Margin + barsWidth * 3 / 4; x += 16)
                for (var y = top + BarHeight + 8; y < top + BarHeight + 8 + TextHeight; y++)
                    for (var dx = 0; dx < 10; dx++) if (glyphs.Next(3) > 0) label[y * lw + x + dx] = 50;
        }
        var random = new Random(29);
        var frame = new byte[Width * Height];
        for (var i = 0; i < frame.Length; i++) frame[i] = (byte)random.Next(70, 150);           // speckled floor
        for (var y = 200; y < 1850; y++) for (var x = 500; x < 2600; x++) frame[y * Width + x] = 200; // bag
        for (var y = 260; y < 1300; y++) for (var x = 700; x < 700 + (y - 260) * 3 / 2 && x < 2500; x++) frame[y * Width + x] = 25; // dark print
        foreach (var (cx, cy) in new[] { (1300, 700), (2100, 1500), (900, 1600) })                // glossy highlights
            for (var y = cy - 60; y < cy + 60; y++) for (var x = cx - 160; x < cx + 160; x++)
                if ((x - cx) * (x - cx) / 25600d + (y - cy) * (y - cy) / 3600d < 1) frame[y * Width + x] = 252;
        double rad = degrees * Math.PI / 180, c = Math.Cos(rad), s = Math.Sin(rad), ox = Width / 2d, oy = Height / 2d;
        for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
            {
                var u = (x - ox) * c + (y - oy) * s + lw / 2d; var v = -(x - ox) * s + (y - oy) * c + lh / 2d;
                if (u < 0 || v < 0 || u >= lw - 1 || v >= lh - 1) continue;
                int u0 = (int)u, v0 = (int)v; double fu = u - u0, fv = v - v0;
                frame[y * Width + x] = (byte)Math.Round(label[v0 * lw + u0] * (1 - fu) * (1 - fv) + label[v0 * lw + u0 + 1] * fu * (1 - fv) +
                    label[(v0 + 1) * lw + u0] * (1 - fu) * fv + label[(v0 + 1) * lw + u0 + 1] * fu * fv);
            }
        var blurred = (byte[])frame.Clone();
        for (var y = 1; y < Height - 1; y++) for (var x = 1; x < Width - 1; x++)
            {
                var i = y * Width + x;
                blurred[i] = (byte)((frame[i] * 4 + (frame[i - 1] + frame[i + 1] + frame[i - Width] + frame[i + Width]) * 2 +
                    frame[i - Width - 1] + frame[i - Width + 1] + frame[i + Width - 1] + frame[i + Width + 1]) / 16);
            }
        return blurred;
    }
}
