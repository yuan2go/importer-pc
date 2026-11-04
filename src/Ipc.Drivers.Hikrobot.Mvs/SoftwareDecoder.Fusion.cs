using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

internal sealed partial class SoftwareDecoder
{
    static bool IsLinear(string format) => format.Replace(" ", "").Replace("-", "") is
        "Code128" or "Code39" or "Code93" or "ITF" or "Codabar" or "EAN8" or "EAN13" or "UPCA" or "UPCE";
    static bool RequiresSourceSupport(string format) => format.Replace(" ", "") is "ITF" or "Code39" or "Codabar";
    bool HasOriginalLineSupport(string format, ImagePoint[] p, byte[] pixels, int width, int height)
    {
        // These symbologies do not require a payload checksum. Strong-checksum
        // formats retain native validation, including blurred Code128 patterns.
        if (!RequiresSourceSupport(format)) return true;
        var x0 = (p[0].X + p[3].X) / 2d; var y0 = (p[0].Y + p[3].Y) / 2d;
        var dx = (p[1].X + p[2].X) / 2d - x0; var dy = (p[1].Y + p[2].Y) / 2d - y0;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 8) return false;
        const int count = 128;
        SpendPatternSamples(count * 9);
        Span<double> reference = stackalloc double[count];
        var mean = 0d; var variance = 0d;
        for (var i = 0; i < count; i++) { var t = (i + .5) / count; reference[i] = Sample(pixels, width, height, x0 + dx * t, y0 + dy * t); mean += reference[i]; }
        mean /= count;
        for (var i = 0; i < count; i++) variance += Math.Pow(reference[i] - mean, 2);
        if (variance < count * 16) return false;
        var consecutive = 0;
        // Native scan counts may include downscaled/sharpened aliases. Require
        // five adjacent physical source lines, independent of payload length.
        for (var offset = -4; offset <= 4; offset++)
        {
            var sum = 0d; var square = 0d; var product = 0d; var inside = true;
            for (var i = 0; i < count; i++)
            {
                var t = (i + .5) / count; var x = x0 + dx * t - dy / length * offset; var y = y0 + dy * t + dx / length * offset;
                inside &= x >= 0 && x < width && y >= 0 && y < height;
                var value = Sample(pixels, width, height, x, y); sum += value; square += value * value; product += (reference[i] - mean) * value;
            }
            var other = square - sum * sum / count;
            consecutive = inside && other >= count * 16 && product / Math.Sqrt(variance * other) >= .8 ? consecutive + 1 : 0;
            if (consecutive >= 5) return true;
        }
        return false;
    }
    void SpendPatternSamples(int count)
    {
        if (count > 8 * 1024 * 1024 - patternSamples) throw new MvsFrameRejection("PatternBudget", "Printed-pattern verification budget exceeded; complete frame rejected");
        patternSamples += count;
    }
    RecognitionSymbol[] PrintedBands(RecognitionSymbol symbol, byte[] pixels, int width, int height)
    {
        var p = symbol.Corners;
        var bandHeight = Math.Max(Math.Sqrt(Math.Pow(p[3].X - p[0].X, 2) + Math.Pow(p[3].Y - p[0].Y, 2)),
            Math.Sqrt(Math.Pow(p[2].X - p[1].X, 2) + Math.Pow(p[2].Y - p[1].Y, 2)));
        var baseline = Math.Sqrt(Math.Pow((p[1].X + p[2].X - p[0].X - p[3].X) / 2d, 2) + Math.Pow((p[1].Y + p[2].Y - p[0].Y - p[3].Y) / 2d, 2));
        if (!IsLinear(symbol.Format) || bandHeight < Math.Max(8, baseline * .45)) return [symbol];
        if (bandHeight > 4096) throw new MvsFrameRejection("PatternBandLimit", "Printed-band height exceeds bounded verification; complete frame rejected");
        SpendPatternSamples(((int)Math.Ceiling(bandHeight) + 6) * 128);
        const int count = 128;
        Span<double> reference = stackalloc double[count]; Span<double> line = stackalloc double[count];
        void Signature(double t, Span<double> values)
        {
            var lx = p[0].X + (p[3].X - p[0].X) * t; var ly = p[0].Y + (p[3].Y - p[0].Y) * t;
            var rx = p[1].X + (p[2].X - p[1].X) * t; var ry = p[1].Y + (p[2].Y - p[1].Y) * t;
            for (var i = 0; i < count; i++) { var along = (i + .5) / count; values[i] = Sample(pixels, width, height, lx + (rx - lx) * along, ly + (ry - ly) * along); }
        }
        static (double Mean, double Variance) Statistics(ReadOnlySpan<double> values)
        {
            var sum = 0d; var square = 0d;
            foreach (var value in values) { sum += value; square += value * value; }
            return (sum / values.Length, square - sum * sum / values.Length);
        }
        var best = 0d;
        foreach (var t in new[] { .05, .25, .5, .75, .95 })
        {
            Signature(t, line); var stats = Statistics(line);
            if (stats.Variance > best) { best = stats.Variance; line.CopyTo(reference); }
        }
        if (best < count * 16) throw new MvsFrameRejection("UnprovenLinearBand", "Linear result has no original-pixel pattern evidence; frame rejected");
        var referenceStats = Statistics(reference); var steps = (int)Math.Ceiling(bandHeight); var start = -1; List<RecognitionSymbol> bands = [];
        for (var step = 0; step <= steps + 1; step++)
        {
            var matches = false;
            if (step <= steps)
            {
                Signature(step / (double)steps, line); var stats = Statistics(line); var product = 0d;
                for (var i = 0; i < count; i++) product += (reference[i] - referenceStats.Mean) * (line[i] - stats.Mean);
                matches = stats.Variance >= count * 16 && product / Math.Sqrt(referenceStats.Variance * stats.Variance) >= .72;
            }
            if (matches && start < 0) start = step;
            if (matches || start < 0) continue;
            if (step - start >= 2)
            {
                ImagePoint Along(ImagePoint top, ImagePoint bottom, double t) => new((int)Math.Round(top.X + (bottom.X - top.X) * t), (int)Math.Round(top.Y + (bottom.Y - top.Y) * t));
                var first = start / (double)steps; var last = (step - 1) / (double)steps;
                bands.Add(symbol with { Corners = [Along(p[0], p[3], first), Along(p[1], p[2], first), Along(p[1], p[2], last), Along(p[0], p[3], last)] });
            }
            start = -1;
        }
        if (bands.Count == 0) throw new MvsFrameRejection("UnprovenLinearBand", "No continuous two-line original barcode pattern; frame rejected");
        return bands.ToArray();
    }
    void MergeCandidate(List<RecognitionSymbol> all, RecognitionSymbol next, byte[] pixels, int width, int height)
    {
        static long Area(RecognitionSymbol symbol)
        {
            var p = symbol.Corners; long area = 0;
            for (var i = 0; i < 4; i++) area += (long)p[i].X * p[(i + 1) % 4].Y - (long)p[i].Y * p[(i + 1) % 4].X;
            return Math.Abs(area);
        }
        var representative = next;
        var insertion = all.Count;
        for (var i = all.Count - 1; i >= 0; i--)
        {
            var prior = all[i];
            if (!SamePrintedRegion(prior, next, pixels, width, height)) continue;
            if (prior.Text != next.Text || prior.Format != next.Format)
                throw new MvsFrameRejection("ConflictingRecognition", "Different valid results cover the same printed region; complete frame rejected");
            // Keep the most complete observed polygon. Keeping only the first
            // partial band loses the geometry needed to associate later searches.
            // A full-band search can also reconcile several earlier partial bands.
            if (Area(prior) > Area(representative)) representative = prior;
            all.RemoveAt(i);
            insertion = i;
        }
        all.Insert(insertion, representative);
        if (all.Count >= CandidateLimit) throw new MvsFrameRejection("CandidateLimit", "Combined candidate limit reached; complete frame rejected");
    }
    bool SamePrintedRegion(RecognitionSymbol a, RecognitionSymbol b, byte[] pixels, int width, int height)
    {
        static (double X, double Y) Mid(ImagePoint a, ImagePoint b) => ((a.X + b.X) / 2d, (a.Y + b.Y) / 2d);
        var a0 = Mid(a.Corners[0], a.Corners[3]); var a1 = Mid(a.Corners[1], a.Corners[2]);
        var b0 = Mid(b.Corners[0], b.Corners[3]); var b1 = Mid(b.Corners[1], b.Corners[2]);
        var dx = a1.X - a0.X; var dy = a1.Y - a0.Y; var size = Math.Sqrt(dx * dx + dy * dy);
        var bdx = b1.X - b0.X; var bdy = b1.Y - b0.Y; var bsize = Math.Sqrt(bdx * bdx + bdy * bdy);
        if (size < 8 || bsize < 8) return a.Corners.SequenceEqual(b.Corners);
        dx /= size; dy /= size;
        var direction = (bdx * dx + bdy * dy) / bsize;
        if (Math.Abs(direction) < .94) return false;
        if (direction < 0) (b0, b1) = (b1, b0);
        // Native linear detection can report a single axis-aligned scan line,
        // while a deskewed pass reports the full oblique band. Use the actual
        // source polygon, not text or expanded bounding boxes, to associate them.
        static bool InPolygon(ImagePoint[] polygon, double x, double y)
        {
            var positive = false; var negative = false;
            for (var i = 0; i < 4; i++)
            {
                var from = polygon[i]; var to = polygon[(i + 1) % 4]; var ex = to.X - from.X; var ey = to.Y - from.Y;
                var cross = ex * (y - from.Y) - ey * (x - from.X); var tolerance = 2 * Math.Sqrt(ex * ex + ey * ey);
                positive |= cross > tolerance; negative |= cross < -tolerance;
            }
            return !(positive && negative);
        }
        static bool ContainsLine(ImagePoint[] polygon, (double X, double Y) from, (double X, double Y) to)
        {
            long twiceArea = 0;
            for (var i = 0; i < 4; i++) twiceArea += (long)polygon[i].X * polygon[(i + 1) % 4].Y - (long)polygon[i].Y * polygon[(i + 1) % 4].X;
            var length = Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));
            if (Math.Abs(twiceArea) < length * 8) return false;
            var inside = 0;
            for (var i = 0; i < 17; i++) { var t = (i + .5) / 17; if (InPolygon(polygon, from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t)) inside++; }
            return inside >= 13;
        }
        var aContains = ContainsLine(a.Corners, b0, b1); var bContains = ContainsLine(b.Corners, a0, a1);
        var aThin = Math.Abs((a.Corners[3].X - a.Corners[0].X) * dy - (a.Corners[3].Y - a.Corners[0].Y) * dx) < Math.Min(32, size * .05);
        var bThin = Math.Abs((b.Corners[3].X - b.Corners[0].X) * bdy - (b.Corners[3].Y - b.Corners[0].Y) * bdx) < bsize * Math.Min(32, bsize * .05);
        if (aContains && bContains || aContains && bThin || bContains && aThin) return true;
        double Along((double X, double Y) p) => (p.X - a0.X) * dx + (p.Y - a0.Y) * dy;
        var lo = Math.Max(0, Along(b0)); var hi = Math.Min(size, Along(b1));
        if (hi - lo < Math.Max(size, bsize) * .85) return false;
        var across = Math.Abs((b0.X - a0.X) * dy - (b0.Y - a0.Y) * dx);
        var aHeight = Math.Sqrt(Math.Pow(a.Corners[3].X - a.Corners[0].X, 2) + Math.Pow(a.Corners[3].Y - a.Corners[0].Y, 2));
        var bHeight = Math.Sqrt(Math.Pow(b.Corners[3].X - b.Corners[0].X, 2) + Math.Pow(b.Corners[3].Y - b.Corners[0].Y, 2));
        // Matrix codes have a genuine 2-D polygon. Linear results may be only a scan line.
        if (!IsLinear(a.Format) || !IsLinear(b.Format))
            return across < Math.Min(aHeight, bHeight) * .25 && Math.Abs(a0.X - b0.X) + Math.Abs(a0.Y - b0.Y) < Math.Min(size, bsize) * .3;
        // Different searches can see a short thick portion and the full height of
        // the same oblique label. Their centre lines need not contain each other.
        // Require near-complete containment in the actual source quadrilateral,
        // in addition to the aligned full encoded extent above. A quiet gap or
        // a separate same-text label does not satisfy this spatial identity.
        static bool ContainsPrintedBand(ImagePoint[] outer, ImagePoint[] inner)
        {
            var inside = 0;
            for (var row = 0; row < 5; row++)
            {
                var down = (row + .5) / 5;
                var lx = inner[0].X + (inner[3].X - inner[0].X) * down;
                var ly = inner[0].Y + (inner[3].Y - inner[0].Y) * down;
                var rx = inner[1].X + (inner[2].X - inner[1].X) * down;
                var ry = inner[1].Y + (inner[2].Y - inner[1].Y) * down;
                for (var column = 0; column < 17; column++)
                {
                    var along = (column + .5) / 17;
                    if (InPolygon(outer, lx + (rx - lx) * along, ly + (ry - ly) * along)) inside++;
                }
            }
            return inside >= 80;
        }
        var nested = (aContains || bContains) && (ContainsPrintedBand(a.Corners, b.Corners) || ContainsPrintedBand(b.Corners, a.Corners));
        if (across > Math.Min(512, size * .6)) return false;
        var steps = Math.Max(1, (int)Math.Ceiling(across));
        SpendPatternSamples((steps + 1) * 128);
        const int samples = 128;
        Span<double> reference = stackalloc double[samples]; Span<double> nextLine = stackalloc double[samples];
        for (var i = 0; i < samples; i++)
        {
            var t = lo + (hi - lo) * (i + .5) / samples;
            reference[i] = Sample(pixels, width, height, a0.X + dx * t, a0.Y + dy * t);
        }
        var mean = 0d; for (var i = 0; i < samples; i++) mean += reference[i]; mean /= samples;
        var variance = 0d; for (var i = 0; i < samples; i++) variance += Math.Pow(reference[i] - mean, 2);
        if (variance < samples * 16) return false;
        for (var step = 1; step <= steps; step++)
        {
            var f = step / (double)steps; var sum = 0d; var square = 0d; var product = 0d;
            for (var i = 0; i < samples; i++)
            {
                var t = lo + (hi - lo) * (i + .5) / samples;
                // Native start/stop endpoints identify the same encoded extent,
                // even when one pass reports an axis-aligned scan line. Match
                // corresponding module fractions, then inspect the pixels between.
                var bt = t / size;
                var ax = a0.X + dx * t; var ay = a0.Y + dy * t;
                var value = Sample(pixels, width, height, ax * (1 - f) + (b0.X + (b1.X - b0.X) * bt) * f,
                    ay * (1 - f) + (b0.Y + (b1.Y - b0.Y) * bt) * f);
                nextLine[i] = value;
                sum += value; square += value * value; product += (reference[i] - mean) * value;
            }
            var otherVariance = square - sum * sum / samples;
            // A quiet gap between two equal printed codes breaks the continuous pattern.
            if (otherVariance < samples * 16 || product / Math.Sqrt(variance * otherVariance) < .72) return false;
            // Nested bands can use slightly different native start/stop extents.
            // Adjacent source lines preserve pattern continuity without accumulated
            // phase drift, while still rejecting every quiet gap between labels.
            if (nested) { nextLine.CopyTo(reference); mean = sum / samples; variance = otherVariance; }
        }
        return true;
    }
    static double Sample(byte[] p, int width, int height, double x, double y)
    {
        x = Math.Clamp(x, 0, width - 1d); y = Math.Clamp(y, 0, height - 1d);
        var ix = (int)x; var iy = (int)y; var nx = Math.Min(ix + 1, width - 1); var ny = Math.Min(iy + 1, height - 1);
        var fx = x - ix; var fy = y - iy;
        return (p[iy * width + ix] * (1 - fx) + p[iy * width + nx] * fx) * (1 - fy) +
            (p[ny * width + ix] * (1 - fx) + p[ny * width + nx] * fx) * fy;
    }
}
