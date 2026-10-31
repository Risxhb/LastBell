// Last Bell mechanic-icon cleanup: removes a baked checkerboard/white padding from
// RGB concept crops, reports clipping and stray fragments, exports 256 px RGBA icons,
// and builds QA contact sheets. Loaded by clean-hostile-icons.ps1 via Add-Type.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

public static class IconTool
{
    const int Canvas = 256, Glyph = 194;

    static byte[] ReadArgb(Bitmap src, out int w, out int h)
    {
        w = src.Width; h = src.Height;
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, w, h);
            var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var buf = new byte[w * h * 4];
            Marshal.Copy(d.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(d);
            return buf; // BGRA
        }
    }

    static Bitmap FromArgb(byte[] buf, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(buf, 0, d.Scan0, buf.Length);
        bmp.UnlockBits(d);
        return bmp;
    }

    static bool BgLike(byte[] p, int i)
    {
        int b = p[i], g = p[i + 1], r = p[i + 2];
        int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
        return mx - mn <= 22 && mn >= 175;
    }

    static int Lum(byte[] p, int i) { return (p[i + 2] * 299 + p[i + 1] * 587 + p[i] * 114) / 1000; }

    // Returns a one-line JSON report.
    public static string Clean(string inPath, string cutPath, string exportPath)
    {
        int w, h;
        byte[] px;
        bool hadAlpha;
        using (var src = new Bitmap(inPath))
        {
            hadAlpha = Image.IsAlphaPixelFormat(src.PixelFormat);
            px = ReadArgb(src, out w, out h);
        }
        int n = w * h;
        // Crop window = bbox of pixels that are not pure padding white.
        int wx0 = w, wy0 = h, wx1 = -1, wy1 = -1;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            if (!(px[i] >= 250 && px[i + 1] >= 250 && px[i + 2] >= 250))
            { wx0 = Math.Min(wx0, x); wy0 = Math.Min(wy0, y); wx1 = Math.Max(wx1, x); wy1 = Math.Max(wy1, y); }
        }

        // 1. Flood fill background-like pixels from the image border.
        var bg = new bool[n];
        var q = new Queue<int>();
        Action<int> seed = k => { if (!bg[k] && BgLike(px, k * 4)) { bg[k] = true; q.Enqueue(k); } };
        for (int x = 0; x < w; x++) { seed(x); seed((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { seed(y * w); seed(y * w + w - 1); }
        while (q.Count > 0)
        {
            int k = q.Dequeue(), x = k % w, y = k / w;
            if (x > 0) seed(k - 1); if (x < w - 1) seed(k + 1);
            if (y > 0) seed(k - w); if (y < h - 1) seed(k + w);
        }

        // 2. Enclosed checker pockets (e.g. a ring's transparent centre): background-like
        //    regions containing both checker tones.
        int enclosedCleared = 0;
        var seen = new bool[n];
        for (int s = 0; s < n; s++)
        {
            if (bg[s] || seen[s] || !BgLike(px, s * 4)) continue;
            var comp = new List<int>(); q.Enqueue(s); seen[s] = true;
            while (q.Count > 0)
            {
                int k = q.Dequeue(); comp.Add(k); int x = k % w, y = k / w;
                foreach (int nb in new[] { x > 0 ? k - 1 : -1, x < w - 1 ? k + 1 : -1, y > 0 ? k - w : -1, y < h - 1 ? k + w : -1 })
                    if (nb >= 0 && !seen[nb] && !bg[nb] && BgLike(px, nb * 4)) { seen[nb] = true; q.Enqueue(nb); }
            }
            int white = comp.Count(k => Lum(px, k * 4) >= 240), gray = comp.Count(k => { int l = Lum(px, k * 4); return l >= 185 && l <= 234; });
            if (comp.Count >= 40 && white >= comp.Count * 0.2 && gray >= comp.Count * 0.2)
            { foreach (int k in comp) bg[k] = true; enclosedCleared++; }
        }

        // 3. Alpha plus edge de-matting against the neighbouring checker colour.
        var outPx = new byte[n * 4];
        for (int k = 0; k < n; k++)
        {
            int i = k * 4;
            if (bg[k]) continue;
            outPx[i] = px[i]; outPx[i + 1] = px[i + 1]; outPx[i + 2] = px[i + 2]; outPx[i + 3] = 255;
            int x = k % w, y = k / w; int bgNb = -1;
            foreach (int nb in new[] { x > 0 ? k - 1 : -1, x < w - 1 ? k + 1 : -1, y > 0 ? k - w : -1, y < h - 1 ? k + w : -1 })
                if (nb >= 0 && bg[nb]) { bgNb = nb; break; }
            if (bgNb < 0) continue;
            int mx = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])), mn = Math.Min(px[i], Math.Min(px[i + 1], px[i + 2]));
            if (mn < 120 || mx - mn > 60) continue; // saturated or dark edge: keep opaque
            double bgl = Lum(px, bgNb * 4), l = Lum(px, i);
            double a = Math.Max(0.0, Math.Min(1.0, (bgl - l) / Math.Max(1.0, bgl - 40)));
            if (a < 0.08) { outPx[i + 3] = 0; continue; }
            for (int c = 0; c < 3; c++)
            {
                double v = (px[i + c] - (1 - a) * px[bgNb * 4 + c]) / a;
                outPx[i + c] = (byte)Math.Max(0, Math.Min(255, v));
            }
            outPx[i + 3] = (byte)(a * 255);
        }

        // 4. Connected components of visible pixels (8-connected).
        var label = new int[n]; for (int k = 0; k < n; k++) label[k] = -1;
        var comps = new List<List<int>>();
        for (int s = 0; s < n; s++)
        {
            if (outPx[s * 4 + 3] == 0 || label[s] >= 0) continue;
            var comp = new List<int>(); int id = comps.Count; label[s] = id; q.Enqueue(s);
            while (q.Count > 0)
            {
                int k = q.Dequeue(); comp.Add(k); int x = k % w, y = k / w;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    int nb = yy * w + xx; if (label[nb] < 0 && outPx[nb * 4 + 3] > 0) { label[nb] = id; q.Enqueue(nb); }
                }
            }
            comps.Add(comp);
        }
        var main = comps.OrderByDescending(c => c.Count).First();
        Func<int, bool> nearWindowEdge = k => { int x = k % w, y = k / w; return x - wx0 <= 2 || wx1 - x <= 2 || y - wy0 <= 2 || wy1 - y <= 2; };
        int removedFragments = 0, removedPixels = 0, keptExtra = 0;
        foreach (var c in comps)
        {
            if (c == main) continue;
            bool touches = c.Any(nearWindowEdge);
            if (c.Count < main.Count * 0.015 || touches)
            { foreach (int k in c) outPx[k * 4 + 3] = 0; removedFragments++; removedPixels += c.Count; }
            else keptExtra++;
        }

        // 5. Clipping: glyph pixels sitting on the crop window's edge.
        var sides = new List<string>();
        int left = main.Count(k => k % w - wx0 <= 1), right = main.Count(k => wx1 - k % w <= 1);
        int top = main.Count(k => k / w - wy0 <= 1), bottom = main.Count(k => wy1 - k / w <= 1);
        if (left > 3) sides.Add("left:" + left); if (right > 3) sides.Add("right:" + right);
        if (top > 3) sides.Add("top:" + top); if (bottom > 3) sides.Add("bottom:" + bottom);

        // 6. Residual checker check: kept pixels that still look like checker grey next to transparency.
        int residual = 0;
        for (int k = 0; k < n; k++)
        {
            if (outPx[k * 4 + 3] < 250 || !BgLike(px, k * 4)) continue;
            int x = k % w, y = k / w;
            if ((x > 0 && outPx[(k - 1) * 4 + 3] == 0) || (x < w - 1 && outPx[(k + 1) * 4 + 3] == 0)) residual++;
        }

        // 7. Exports: the cut-out at original placement, and a normalised 256 px icon.
        using (var cut = FromArgb(outPx, w, h)) cut.Save(cutPath, ImageFormat.Png);
        int bw, bh; double scale;
        Export(outPx, w, h, exportPath, out bw, out bh, out scale);

        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{{\"file\":\"{0}\",\"sourceHasAlpha\":{1},\"window\":[{2},{3},{4},{5}],\"glyphPixels\":{6},\"enclosedCheckerCleared\":{7},\"fragmentsRemoved\":{8},\"fragmentPixelsRemoved\":{9},\"extraPartsKept\":{10},\"clippedSides\":[{11}],\"residualCheckerEdgePixels\":{12},\"glyphBox\":[{13},{14}],\"scale\":{15:0.00}}}",
            Path.GetFileName(inPath), hadAlpha ? "true" : "false", wx0, wy0, wx1, wy1, main.Count, enclosedCleared, removedFragments, removedPixels, keptExtra,
            string.Join(",", sides.Select(s => "\"" + s + "\"")), residual, bw, bh, scale);
    }

    // Trims transparent margins and fits the glyph into Glyph x Glyph, centred on a Canvas square.
    static void Export(byte[] outPx, int w, int h, string exportPath, out int bw, out int bh, out double scale)
    {
        int bx0 = w, by0 = h, bx1 = -1, by1 = -1;
        for (int k = 0; k < w * h; k++) if (outPx[k * 4 + 3] > 0) { int x = k % w, y = k / w; bx0 = Math.Min(bx0, x); by0 = Math.Min(by0, y); bx1 = Math.Max(bx1, x); by1 = Math.Max(by1, y); }
        bw = bx1 - bx0 + 1; bh = by1 - by0 + 1;
        scale = (double)Glyph / Math.Max(bw, bh);
        using (var cut = FromArgb(outPx, w, h))
        using (var dst = new Bitmap(Canvas, Canvas, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingMode = CompositingMode.SourceCopy;
                int dw = (int)Math.Round(bw * scale), dh = (int)Math.Round(bh * scale);
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(cut, new Rectangle((Canvas - dw) / 2, (Canvas - dh) / 2, dw, dh), bx0, by0, bw, bh, GraphicsUnit.Pixel, ia);
                }
            }
            dst.Save(exportPath, ImageFormat.Png);
        }
    }

    // For regenerated art delivered on a flat chroma-key background (default pure green
    // #00FF00), or with genuine alpha. Keys the background, removes green spill, reports
    // clipping against the image border and exports a 256 px icon. Returns one-line JSON.
    public static string CleanChroma(string inPath, string exportPath, string keyHex)
    {
        int w, h; byte[] px; bool hadAlpha;
        using (var src = new Bitmap(inPath)) { hadAlpha = Image.IsAlphaPixelFormat(src.PixelFormat); px = ReadArgb(src, out w, out h); }
        var key = ColorTranslator.FromHtml(keyHex);
        var outPx = (byte[])px.Clone();
        int keyed = 0, translucentSource = 0;
        for (int k = 0; k < w * h; k++)
        {
            int i = k * 4;
            if (px[i + 3] < 255) translucentSource++;
            double dr = px[i + 2] - key.R, dg = px[i + 1] - key.G, db = px[i] - key.B;
            double dist = Math.Sqrt(dr * dr + dg * dg + db * db);
            double a = Math.Max(0, Math.Min(1, (dist - 60) / 90.0)); // fully key within 60, opaque beyond 150
            a = Math.Min(a, px[i + 3] / 255.0);
            if (a < 1) keyed++;
            if (key.G == 255 && key.R == 0 && key.B == 0) // green spill suppression
            { int lim = Math.Max(px[i + 2], px[i]); if (outPx[i + 1] > lim) outPx[i + 1] = (byte)lim; }
            outPx[i + 3] = (byte)(a * 255);
        }
        // Clipping = opaque glyph pixels on the image border.
        int edge = 0;
        for (int x = 0; x < w; x++) { if (outPx[x * 4 + 3] > 128) edge++; if (outPx[((h - 1) * w + x) * 4 + 3] > 128) edge++; }
        for (int y = 0; y < h; y++) { if (outPx[(y * w) * 4 + 3] > 128) edge++; if (outPx[(y * w + w - 1) * 4 + 3] > 128) edge++; }
        int bw, bh; double scale;
        Export(outPx, w, h, exportPath, out bw, out bh, out scale);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{{\"file\":\"{0}\",\"sourceHasAlpha\":{1},\"sourceTranslucentPixels\":{2},\"keyedPixels\":{3},\"borderPixels\":{4},\"clipped\":{5},\"glyphBox\":[{6},{7}],\"scale\":{8:0.00}}}",
            Path.GetFileName(inPath), hadAlpha ? "true" : "false", translucentSource, keyed, edge, edge > 3 ? "true" : "false", bw, bh, scale);
    }

    // Labeled grid. cell = icon draw size; gray = convert icons to luminance.
    public static void Sheet(string[] files, string[] labels, string outPath, int cell, int cols, string bgHex, string fgHex, bool gray)
    {
        int pad = 10, labelH = cell >= 96 ? 22 : 16;
        int rows = (files.Length + cols - 1) / cols;
        int cw = Math.Max(cell, 150) + pad, ch = cell + labelH + pad;
        using (var sheet = new Bitmap(cols * cw + pad, rows * ch + pad, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(sheet))
        using (var font = new Font("Segoe UI", cell >= 96 ? 9f : 7f))
        using (var brush = new SolidBrush(ColorTranslator.FromHtml(fgHex)))
        {
            g.Clear(ColorTranslator.FromHtml(bgHex));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var cm = new ColorMatrix(new float[][] {
                new float[] { .299f, .299f, .299f, 0, 0 }, new float[] { .587f, .587f, .587f, 0, 0 },
                new float[] { .114f, .114f, .114f, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { 0, 0, 0, 0, 1 } });
            for (int i = 0; i < files.Length; i++)
            {
                int cx = pad + (i % cols) * cw, cy = pad + (i / cols) * ch;
                int ox = cx + (cw - pad - cell) / 2;
                if (files[i] != null && File.Exists(files[i]))
                    using (var img = new Bitmap(files[i]))
                    using (var ia = new ImageAttributes())
                    {
                        if (gray) ia.SetColorMatrix(cm);
                        g.DrawImage(img, new Rectangle(ox, cy, cell, cell), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                    }
                else
                    using (var pen = new Pen(brush, 1) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, ox, cy, cell - 1, cell - 1);
                var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                g.DrawString(labels[i], font, brush, new RectangleF(cx, cy + cell + 2, cw - pad, labelH), sf);
            }
            sheet.Save(outPath, ImageFormat.Png);
        }
    }
}
