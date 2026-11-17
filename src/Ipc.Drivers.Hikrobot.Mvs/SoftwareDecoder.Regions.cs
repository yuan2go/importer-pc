using System.Buffers;
using Ipc.Platform;
using ZXingCpp;

namespace Ipc.Drivers.Hikrobot.Mvs;

internal sealed partial class SoftwareDecoder
{
    const int Tile = 32, RegionLimit = 32, RegionPixelLimit = 4 * 1024 * 1024;
    // Centers: the bar-dense tiles of the region (at least half its median edge density), in original-image pixels.
    sealed record SearchRegion(int X, int Y, int Width, int Height, double Angle, double AlternateAngle, int Tiles, (double X, double Y)[] Centers, double BarAngle);
    struct EdgeTile { public double XX, YY, XY, Angle, Density; public bool Active; }
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
                cells[cy * columns + cx] = new()
                {
                    XX = xx,
                    YY = yy,
                    XY = xy,
                    Angle = Math.Atan2(2 * xy, xx - yy) / 2,
                    Density = count == 0 ? 0 : (double)strong / count,
                    Active = count > 0 && strong >= count * .16 && coherence >= .8
                };
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
            var barAngle = angle;
            if (angle > Math.PI / 4) angle -= Math.PI / 2;
            if (angle < -Math.PI / 4) angle += Math.PI / 2;
            if (alternate > Math.PI / 4) alternate -= Math.PI / 2;
            if (alternate < -Math.PI / 4) alternate += Math.PI / 2;
            // Tiles a recognised code must explain: bar-dense ones. A single straight edge (label paper, box flap)
            // joins the component but is far sparser than bars; a second printed code is as dense as the first.
            var densities = component.Select(at => cells[at].Density).Order().ToArray(); var dense = densities[densities.Length / 2] / 2;
            var centers = component.Where(at => cells[at].Density >= dense)
                .Select(at => (Math.Min(width - 1, (at % columns + .5) * Tile), Math.Min(height - 1, (at / columns + .5) * Tile))).ToArray();
            result.Add(new(left, top, right - left, bottom - top, angle, alternate, tiles, centers, barAngle));
            if (result.Count > RegionLimit) throw new MvsFrameRejection("SearchRegionLimit", "Directional search capacity exceeded; complete frame rejected");
        }
        return result;
    }
    // A region is explained once every barcode-like tile in it lies on a printed code already recognised.
    // Its remaining deskewed passes could only find that same code again, so they are skipped. A tile that no
    // recognised code explains - a second or still unread code - keeps the full search, so a real second code is
    // still found and the frame is still rejected as multiple codes.
    // Only a box that the original pixels show to be one continuous printed band can explain tiles.
    bool Explained(SearchRegion region, List<RecognitionSymbol> symbols, byte[] original, int width, int height)
    {
        if (symbols.Count == 0) return false;
        var bands = symbols.Where(s =>
            Math.Abs(Math.Cos(Math.Atan2(s.Corners[1].Y - s.Corners[0].Y, s.Corners[1].X - s.Corners[0].X) - region.BarAngle)) >= .94 &&
            SingleBand(s, original, width, height)).ToArray();
        return bands.Length > 0 && region.Centers.All(c => bands.Any(s => CoversTile(s, c.X, c.Y, original, width, height)));
    }
    struct UncoveredBandRow
    {
        public double XX, YY, XY, Low, High;
        public int Count, Strong;
    }
    bool CoversTile(RecognitionSymbol symbol, double centerX, double centerY, byte[] original, int width, int height)
    {
        var q = symbol.Corners;
        if (Covers(q, centerX - Tile / 2d, centerY - Tile / 2d) && Covers(q, centerX + Tile / 2d, centerY - Tile / 2d) &&
            Covers(q, centerX + Tile / 2d, centerY + Tile / 2d) && Covers(q, centerX - Tile / 2d, centerY + Tile / 2d)) return true;
        double sx = (q[0].X + q[3].X) / 2d, sy = (q[0].Y + q[3].Y) / 2d;
        double dx = (q[1].X + q[2].X) / 2d - sx, dy = (q[1].Y + q[2].Y) / 2d - sy;
        var length = Math.Sqrt(dx * dx + dy * dy); if (length < 1) return false;
        double ux = dx / length, uy = dy / length;
        // Tile centres can lie outside a short/slanted band while every dense bar
        // edge in the tile lies inside it. Examine those original edges instead of
        // expanding the band by a whole tile, which could cover another label.
        SpendPatternSamples(Tile * Tile / 4 * 8);
        var minimum = 0d; var maximum = 0d;
        Span<UncoveredBandRow> rows = stackalloc UncoveredBandRow[Tile];
        var centerAcross = -(centerX - sx) * uy + (centerY - sy) * ux;
        var outsideMinimum = double.MaxValue; var outsideMaximum = double.MinValue;
        for (var y = centerY - Tile / 2; y < centerY + Tile / 2; y += 2)
            for (var x = centerX - Tile / 2; x < centerX + Tile / 2; x += 2)
            {
                if (x < 1 || y < 1 || x >= width - 1 || y >= height - 1 || Covers(q, x, y)) continue;
                var nw = Sample(original, width, height, x - 1, y - 1); var n = Sample(original, width, height, x, y - 1);
                var ne = Sample(original, width, height, x + 1, y - 1); var e = Sample(original, width, height, x + 1, y);
                var se = Sample(original, width, height, x + 1, y + 1); var s = Sample(original, width, height, x, y + 1);
                var sw = Sample(original, width, height, x - 1, y + 1); var w = Sample(original, width, height, x - 1, y);
                var gx = (ne + 2 * e + se - nw - 2 * w - sw) / 4;
                var gy = (sw + 2 * s + se - nw - 2 * n - ne) / 4;
                var along = (x - sx) * ux + (y - sy) * uy;
                if (along < 0 || along > length)
                {
                    if (along < -Math.Max(2 * Tile, .18 * length) || along > length + Math.Max(2 * Tile, .18 * length)) return false;
                    if (Math.Abs(gx) + Math.Abs(gy) >= 20)
                    { outsideMinimum = Math.Min(outsideMinimum, along); outsideMaximum = Math.Max(outsideMaximum, along); }
                    continue;
                }
                var across = -(x - sx) * uy + (y - sy) * ux;
                ref var row = ref rows[(int)Math.Floor((across - centerAcross + Tile) / 2)];
                var parallel = gx * ux + gy * uy; var normal = -gx * uy + gy * ux;
                row.XX += parallel * parallel; row.YY += normal * normal; row.XY += parallel * normal;
                if (row.Count++ == 0) row.Low = row.High = across;
                row.Low = Math.Min(row.Low, across); row.High = Math.Max(row.High, across);
                if (Math.Abs(gx) + Math.Abs(gy) >= 20) row.Strong++;
            }
        // Apply the same original-image density/coherence rule as FindRegions to
        // the uncovered scan rows. Random glyph texture is not a second bar band.
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            if (row.Count == 0) continue;
            double xx = 0, yy = 0, xy = 0; var count = 0; var strong = 0;
            for (var nearby = Math.Max(0, index - 1); nearby <= Math.Min(rows.Length - 1, index + 1); nearby++)
            { xx += rows[nearby].XX; yy += rows[nearby].YY; xy += rows[nearby].XY; count += rows[nearby].Count; strong += rows[nearby].Strong; }
            var energy = xx + yy;
            if (count < 8 || strong < count * .16 || energy == 0 || xx < yy ||
                Math.Sqrt(Math.Pow(xx - yy, 2) + 4 * xy * xy) / energy < .8) continue;
            minimum = Math.Min(minimum, row.Low); maximum = Math.Max(maximum, row.High);
        }
        bool Continuous(double across)
        {
            if (across == 0) return true;
            var center = (centerX - sx) * ux + (centerY - sy) * uy;
            var lo = Math.Max(0, center - 2 * Tile); var hi = Math.Min(length, center + 2 * Tile);
            ImagePoint Point(double along, double offset) => new((int)Math.Round(sx + ux * along - uy * offset), (int)Math.Round(sy + uy * along + ux * offset));
            var a = Point(lo, 0); var b = Point(hi, 0); var c = Point(lo, across); var d = Point(hi, across);
            return SamePrintedRegion(symbol with { Corners = [a, b, b, a] }, symbol with { Corners = [c, d, d, c] }, original, width, height, adjacentLines: true);
        }
        var lowContinuous = Continuous(minimum); var highContinuous = Continuous(maximum);
        return outsideMaximum - outsideMinimum <= 4 && lowContinuous && highContinuous;
    }
    // The code box as an oriented band. Along the code axis the quiet zone and the label paper corner beyond it are
    // allowed: two tiles or 18% of the code length. A second code placed end to end keeps a 20-module gap and is at
    // least 35 modules long, so it never lies wholly inside that allowance and keeps the full search.
    // Across the code, unexplained tiles remain searchable: a tile-sized margin can hide a short stacked label.
    static bool Covers(ImagePoint[] q, double x, double y)
    {
        double sx = (q[0].X + q[3].X) / 2d, sy = (q[0].Y + q[3].Y) / 2d, ex = (q[1].X + q[2].X) / 2d, ey = (q[1].Y + q[2].Y) / 2d;
        var length = Math.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy));
        if (length < 1) return false;
        double ux = (ex - sx) / length, uy = (ey - sy) / length;
        var along = (x - sx) * ux + (y - sy) * uy;
        double across = -(x - sx) * uy + (y - sy) * ux, low = double.MaxValue, high = double.MinValue;
        foreach (var corner in q) { var v = -(corner.X - sx) * uy + (corner.Y - sy) * ux; low = Math.Min(low, v); high = Math.Max(high, v); }
        var outsideAlong = Math.Max(0, Math.Max(-along, along - length));
        var outsideAcross = Math.Max(0, Math.Max(low - across, across - high));
        return outsideAlong <= Math.Max(2 * Tile, .18 * length) && outsideAcross == 0;
    }
    void SearchRegions(byte[] original, int width, int height, List<RecognitionSymbol> symbols)
    {
        var used = 0;
        foreach (var region in FindRegions(original, width, height))
        {
            if (Explained(region, symbols, original, width, height)) { timings.Record("regions.explained-before-search", 0); continue; }
            var explained = false;
            foreach (var direction in Math.Abs(region.Angle - region.AlternateAngle) >= Math.PI / 360 ? new[] { region.Angle, region.AlternateAngle } : new[] { region.Angle })
            {
                if (explained) break;
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
                    // Passes in increasing cost and specificity; stop as soon as the region is explained.
                    bool Pass(BarcodeReader engine, byte[] image)
                    {
                        ReadPass(engine, image, w, h, symbols, original, width, height, Point);
                        return explained = Explained(region, symbols, original, width, height);
                    }
                    if (Pass(reader!, rotated) || Pass(thresholdReader!, rotated)) { timings.Record("regions.explained-early", 0); continue; }
                    Sharpen(rotated, enhanced, w, h);
                    foreach (var threshold in new[] { 80, 104, 128, 152, 176 })
                    {
                        for (var i = 0; i < length; i++) binary[i] = enhanced[i] < threshold ? (byte)0 : (byte)255;
                        if (Pass(thresholdReader!, binary)) break;
                    }
                    if (explained) { timings.Record("regions.explained-sharpened", 0); continue; }
                    SharpenWide(rotated, enhanced, binary, w, h);
                    foreach (var threshold in new[] { 80, 104, 128, 152, 176 })
                    {
                        for (var i = 0; i < length; i++) binary[i] = enhanced[i] < threshold ? (byte)0 : (byte)255;
                        if (Pass(thresholdReader!, binary)) break;
                    }
                    if (!explained) timings.Record("regions.full-search", 0);
                }
                finally
                {
                    if (rotated is not null) ArrayPool<byte>.Shared.Return(rotated, true);
                    if (enhanced is not null) ArrayPool<byte>.Shared.Return(enhanced, true);
                    if (binary is not null) ArrayPool<byte>.Shared.Return(binary, true);
                }
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
