using System.Buffers;
using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

internal sealed partial class SoftwareDecoder
{
    const int Tile = 32, RegionLimit = 32, RegionPixelLimit = 4 * 1024 * 1024;
    sealed record SearchRegion(int X, int Y, int Width, int Height, double Angle, double AlternateAngle, int Tiles);
    struct EdgeTile { public double XX, YY, XY, Angle; public bool Active; }
    static List<SearchRegion> FindRegions(byte[] p, int width, int height)
    {
        var columns = (width + Tile - 1) / Tile; var rows = (height + Tile - 1) / Tile;
        var cells = new EdgeTile[checked(columns * rows)];
        for (var cy = 0; cy < rows; cy++) for (var cx = 0; cx < columns; cx++)
        {
            double xx = 0, yy = 0, xy = 0; var strong = 0; var count = 0;
            for (var y = Math.Max(1, cy * Tile); y < Math.Min(height - 1, (cy + 1) * Tile); y += 2)
                for (var x = Math.Max(1, cx * Tile); x < Math.Min(width - 1, (cx + 1) * Tile); x += 2)
                {
                    var i = y * width + x;
                    var gx = (p[i - width + 1] + 2 * p[i + 1] + p[i + width + 1] - p[i - width - 1] - 2 * p[i - 1] - p[i + width - 1]) / 4;
                    var gy = (p[i + width - 1] + 2 * p[i + width] + p[i + width + 1] - p[i - width - 1] - 2 * p[i - width] - p[i - width + 1]) / 4;
                    xx += gx * gx; yy += gy * gy; xy += gx * gy; count++;
                    if (Math.Abs(gx) + Math.Abs(gy) >= 20) strong++;
                }
            var energy = xx + yy; var coherence = energy == 0 ? 0 : Math.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy) / energy;
            cells[cy * columns + cx] = new() { XX = xx, YY = yy, XY = xy, Angle = Math.Atan2(2 * xy, xx - yy) / 2,
                Active = count > 0 && strong >= count * .16 && coherence >= .8 };
        }
        var result = new List<SearchRegion>(); var seen = new bool[cells.Length]; var todo = new Queue<int>();
        for (var start = 0; start < cells.Length; start++)
        {
            if (seen[start] || !cells[start].Active) continue;
            todo.Enqueue(start); seen[start] = true;
            var component = new List<int>();
            var minX = columns; var minY = rows; var maxX = 0; var maxY = 0; double xx = 0, yy = 0, xy = 0; var tiles = 0;
            while (todo.TryDequeue(out var at))
            {
                var x = at % columns; var y = at / columns; minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                var c = cells[at]; xx += c.XX; yy += c.YY; xy += c.XY; tiles++;
                component.Add(at);
                for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx; var ny = y + dy;
                    if (nx < 0 || nx >= columns || ny < 0 || ny >= rows) continue;
                    var next = ny * columns + nx;
                    if (seen[next] || !cells[next].Active || Math.Abs(Math.Cos(c.Angle - cells[next].Angle)) < .9) continue;
                    seen[next] = true; todo.Enqueue(next);
                }
            }
            if (tiles < 6) continue;
            var angle = Math.Atan2(2 * xy, xx - yy) / 2;
            // A second derivative scale avoids a blur-dependent angular bias.
            double fineXX = 0, fineYY = 0, fineXY = 0;
            foreach (var at in component)
                for (var y = Math.Max(1, at / columns * Tile); y < Math.Min(height - 1, (at / columns + 1) * Tile); y += 2)
                    for (var x = Math.Max(1, at % columns * Tile); x < Math.Min(width - 1, (at % columns + 1) * Tile); x += 2)
                    {
                        var i = y * width + x; var gx = p[i + 1] - p[i - 1]; var gy = p[i + width] - p[i - width];
                        fineXX += gx * gx; fineYY += gy * gy; fineXY += gx * gy;
                    }
            var alternate = Math.Atan2(2 * fineXY, fineXX - fineYY) / 2;
            var ca = Math.Cos(angle); var sa = Math.Sin(angle);
            var minAlong = double.PositiveInfinity; var maxAlong = double.NegativeInfinity;
            var minAcross = double.PositiveInfinity; var maxAcross = double.NegativeInfinity;
            foreach (var at in component)
            {
                var x = (at % columns + .5) * Tile; var y = (at / columns + .5) * Tile;
                var along = x * ca + y * sa; var across = -x * sa + y * ca;
                minAlong = Math.Min(minAlong, along); maxAlong = Math.Max(maxAlong, along);
                minAcross = Math.Min(minAcross, across); maxAcross = Math.Max(maxAcross, across);
            }
            // Dense repeating edges must extend across the bars. Long bag seams
            // and textured backgrounds extend along edges and are not 1-D regions.
            if (maxAlong - minAlong + Tile < 1.6 * (maxAcross - minAcross + Tile)) continue;
            var margin = Math.Max(48, Math.Max(maxX - minX, maxY - minY) * Tile / 8);
            var left = Math.Max(0, minX * Tile - margin); var top = Math.Max(0, minY * Tile - margin);
            var right = Math.Min(width, (maxX + 1) * Tile + margin); var bottom = Math.Min(height, (maxY + 1) * Tile + margin);
            if (angle > Math.PI / 4) angle -= Math.PI / 2;
            if (angle < -Math.PI / 4) angle += Math.PI / 2;
            if (alternate > Math.PI / 4) alternate -= Math.PI / 2;
            if (alternate < -Math.PI / 4) alternate += Math.PI / 2;
            result.Add(new(left, top, right - left, bottom - top, angle, alternate, tiles));
            if (result.Count > RegionLimit) throw new MvsFrameRejection("SearchRegionLimit", "Directional search capacity exceeded; complete frame rejected");
        }
        return result;
    }
    void SearchRegions(byte[] original, int width, int height, List<RecognitionSymbol> symbols)
    {
        var used = 0;
        foreach (var region in FindRegions(original, width, height))
        foreach (var direction in Math.Abs(region.Angle - region.AlternateAngle) >= Math.PI / 360 ? new[] { region.Angle, region.AlternateAngle } : new[] { region.Angle })
        {
            var angle = -direction; var cos = Math.Cos(angle); var sin = Math.Sin(angle);
            var w = (int)Math.Ceiling(region.Width * Math.Abs(cos) + region.Height * Math.Abs(sin));
            var h = (int)Math.Ceiling(region.Height * Math.Abs(cos) + region.Width * Math.Abs(sin));
            var length = checked(w * h); used = checked(used + length);
            if (used > RegionPixelLimit) throw new MvsFrameRejection("SearchPixelLimit", "Directional search pixel budget exceeded; complete frame rejected");
            byte[]? rotated = null, enhanced = null, binary = null;
            try
            {
                rotated = ArrayPool<byte>.Shared.Rent(length); enhanced = ArrayPool<byte>.Shared.Rent(length); binary = ArrayPool<byte>.Shared.Rent(length);
                var centerX = region.X + (region.Width - 1) / 2d; var centerY = region.Y + (region.Height - 1) / 2d;
                (double X, double Y) Map(double x, double y) => (centerX + cos * (x - (w - 1) / 2d) + sin * (y - (h - 1) / 2d),
                    centerY - sin * (x - (w - 1) / 2d) + cos * (y - (h - 1) / 2d));
                ImagePoint Point(int x, int y) { var p = Map(x, y); return new((int)Math.Round(p.X), (int)Math.Round(p.Y)); }
                for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
                {
                    var p = Map(x, y);
                    rotated[y * w + x] = p.X < region.X || p.X >= region.X + region.Width || p.Y < region.Y || p.Y >= region.Y + region.Height ? (byte)255 :
                        (byte)Math.Round(Sample(original, width, height, p.X, p.Y));
                }
                ReadPass(reader!, rotated, w, h, symbols, original, width, height, Point);
                ReadPass(thresholdReader!, rotated, w, h, symbols, original, width, height, Point);
                Sharpen(rotated, enhanced, w, h);
                foreach (var threshold in new[] { 80, 104, 128, 152, 176 })
                {
                    for (var i = 0; i < length; i++) binary[i] = enhanced[i] < threshold ? (byte)0 : (byte)255;
                    ReadPass(thresholdReader!, binary, w, h, symbols, original, width, height, Point);
                }
                SharpenWide(rotated, enhanced, binary, w, h);
                foreach (var threshold in new[] { 80, 104, 128, 152, 176 })
                {
                    for (var i = 0; i < length; i++) binary[i] = enhanced[i] < threshold ? (byte)0 : (byte)255;
                    ReadPass(thresholdReader!, binary, w, h, symbols, original, width, height, Point);
                }
            }
            finally
            {
                if (rotated is not null) ArrayPool<byte>.Shared.Return(rotated, true);
                if (enhanced is not null) ArrayPool<byte>.Shared.Return(enhanced, true);
                if (binary is not null) ArrayPool<byte>.Shared.Return(binary, true);
            }
        }
    }
    static void Sharpen(byte[] source, byte[] target, int w, int h)
    {
        source.AsSpan(0, w * h).CopyTo(target);
        for (var y = 1; y < h - 1; y++) for (var x = 1; x < w - 1; x++)
        {
            var i = y * w + x;
            var blurred = (source[i] * 4 + (source[i - 1] + source[i + 1] + source[i - w] + source[i + w]) * 2 +
                source[i - w - 1] + source[i - w + 1] + source[i + w - 1] + source[i + w + 1]) / 16;
            target[i] = (byte)Math.Clamp(source[i] * 3 - blurred * 2, 0, 255);
        }
    }
    static void SharpenWide(byte[] source, byte[] target, byte[] work, int w, int h)
    {
        ReadOnlySpan<int> kernel = [1, 4, 6, 4, 1];
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var sum = 0;
            for (var k = -2; k <= 2; k++) sum += source[y * w + Math.Clamp(x + k, 0, w - 1)] * kernel[k + 2];
            work[y * w + x] = (byte)(sum / 16);
        }
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var sum = 0;
            for (var k = -2; k <= 2; k++) sum += work[Math.Clamp(y + k, 0, h - 1) * w + x] * kernel[k + 2];
            target[y * w + x] = (byte)Math.Clamp(source[y * w + x] * 5 - sum / 16 * 4, 0, 255);
        }
    }
}
