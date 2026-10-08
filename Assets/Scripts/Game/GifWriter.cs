using System;
using System.Collections.Generic;
using System.IO;

namespace HWC.Gameplay
{
    /// <summary>
    /// An animated GIF encoder in plain C# (no Unity, no package, no ffmpeg), for the trip clips. One palette
    /// for the whole clip (median cut over every frame), a light ordered dither against banding, and each
    /// frame stores only the rectangle that changed, with unchanged pixels transparent, so a still bench or
    /// a slow camera costs little. Loops forever.
    /// </summary>
    public static class GifWriter
    {
        const int Transparent = 255;   // palette index kept for "same as the frame before"

        /// <summary>Frames are RGB24, bottom row first (as Unity reads them back); delay in hundredths of a second.
        /// The dither smooths gradients and costs about a fifth more bytes.</summary>
        public static void Write(Stream output, IList<byte[]> frames, int width, int height, int delayCs, bool dither = true)
        {
            if (frames.Count == 0) throw new ArgumentException("no frames");
            var palette = BuildPalette(frames, width, height, 255);
            var lut = BuildLookup(palette);

            var w = new BinaryWriter(output);
            w.Write(new[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' });
            w.Write((ushort)width); w.Write((ushort)height);
            w.Write((byte)0xF7);   // global colour table, 8 bits per colour, 256 entries
            w.Write((byte)0); w.Write((byte)0);
            for (int i = 0; i < 256; i++)
            {
                int c = i < palette.Count ? palette[i] : 0;
                w.Write((byte)(c >> 16)); w.Write((byte)(c >> 8)); w.Write((byte)c);
            }
            // NETSCAPE2.0: loop forever
            w.Write(new byte[] { 0x21, 0xFF, 0x0B });
            w.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            w.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });

            byte[] prev = null;
            var cur = new byte[width * height];
            for (int f = 0; f < frames.Count; f++)
            {
                Index(frames[f], width, height, lut, cur, dither);
                int x0 = 0, y0 = 0, x1 = width - 1, y1 = height - 1;
                if (prev != null && !ChangedRect(prev, cur, width, height, out x0, out y0, out x1, out y1))
                {
                    x0 = y0 = x1 = y1 = 0;   // nothing changed: one transparent pixel keeps the timing
                }
                int rw = x1 - x0 + 1, rh = y1 - y0 + 1;
                var px = new byte[rw * rh];
                for (int y = 0; y < rh; y++)
                    for (int x = 0; x < rw; x++)
                    {
                        int i = (y0 + y) * width + x0 + x;
                        px[y * rw + x] = prev != null && prev[i] == cur[i] ? (byte)Transparent : cur[i];
                    }
                // graphic control: leave the frame in place, transparent index, delay
                w.Write(new byte[] { 0x21, 0xF9, 0x04, (byte)(0x04 | (prev != null ? 0x01 : 0x00)) });
                w.Write((ushort)delayCs);
                w.Write((byte)Transparent);
                w.Write((byte)0);
                w.Write((byte)0x2C);
                w.Write((ushort)x0); w.Write((ushort)y0); w.Write((ushort)rw); w.Write((ushort)rh);
                w.Write((byte)0);
                Lzw(w, px, 8);
                if (prev == null) prev = new byte[cur.Length];
                Buffer.BlockCopy(cur, 0, prev, 0, cur.Length);
            }
            w.Write((byte)0x3B);
            w.Flush();
        }

        static bool ChangedRect(byte[] a, byte[] b, int w, int h, out int x0, out int y0, out int x1, out int y1)
        {
            x0 = w; y0 = h; x1 = -1; y1 = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (a[row + x] == b[row + x]) continue;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    y1 = y;
                }
            }
            return x1 >= 0;
        }

        // ---- palette: median cut over a 5-bit-per-channel histogram ---------------------------------

        static int Key5(int r, int g, int b) => (r >> 3) << 10 | (g >> 3) << 5 | (b >> 3);

        static List<int> BuildPalette(IList<byte[]> frames, int width, int height, int colours)
        {
            var hist = new int[32768];
            int step = Math.Max(1, frames.Count / 24);   // two dozen frames are plenty to find the colours
            for (int f = 0; f < frames.Count; f += step)
            {
                var px = frames[f];
                for (int i = 0; i < width * height; i++) hist[Key5(px[i * 3], px[i * 3 + 1], px[i * 3 + 2])]++;
            }
            var used = new List<int>();
            for (int k = 0; k < hist.Length; k++) if (hist[k] > 0) used.Add(k);
            var boxes = new List<List<int>> { used };
            while (boxes.Count < colours)
            {
                // split the box with the widest channel range (weighted a little by population)
                int best = -1; long bestScore = 0; int bestCh = 0;
                for (int bi = 0; bi < boxes.Count; bi++)
                {
                    var box = boxes[bi];
                    if (box.Count < 2) continue;
                    int ch = WidestChannel(box, out int range);
                    long pop = 0; foreach (int k in box) pop += hist[k];
                    long score = (long)range * range * (long)Math.Sqrt(pop + 1);
                    if (score > bestScore) { bestScore = score; best = bi; bestCh = ch; }
                }
                if (best < 0) break;
                var split = boxes[best];
                int shift = bestCh == 0 ? 10 : bestCh == 1 ? 5 : 0;
                split.Sort((a, b) => ((a >> shift) & 31).CompareTo((b >> shift) & 31));
                long total = 0; foreach (int k in split) total += hist[k];
                long half = total / 2, run = 0; int cut = 1;
                for (int i = 0; i < split.Count - 1; i++) { run += hist[split[i]]; if (run >= half) { cut = i + 1; break; } cut = i + 1; }
                boxes[best] = split.GetRange(0, cut);
                boxes.Add(split.GetRange(cut, split.Count - cut));
            }
            var pal = new List<int>();
            foreach (var box in boxes)
            {
                long r = 0, g = 0, b = 0, n = 0;
                foreach (int k in box)
                {
                    long c = hist[k];
                    r += (((k >> 10) & 31) * 8 + 4) * c; g += (((k >> 5) & 31) * 8 + 4) * c; b += ((k & 31) * 8 + 4) * c; n += c;
                }
                if (n == 0) continue;
                pal.Add((int)(r / n) << 16 | (int)(g / n) << 8 | (int)(b / n));
            }
            if (pal.Count == 0) pal.Add(0);
            return pal;
        }

        static int WidestChannel(List<int> box, out int range)
        {
            int[] lo = { 31, 31, 31 }, hi = { 0, 0, 0 };
            foreach (int k in box)
            {
                int r = (k >> 10) & 31, g = (k >> 5) & 31, b = k & 31;
                if (r < lo[0]) lo[0] = r; if (r > hi[0]) hi[0] = r;
                if (g < lo[1]) lo[1] = g; if (g > hi[1]) hi[1] = g;
                if (b < lo[2]) lo[2] = b; if (b > hi[2]) hi[2] = b;
            }
            int ch = 0;
            range = hi[0] - lo[0];
            if (hi[1] - lo[1] > range) { range = hi[1] - lo[1]; ch = 1; }
            if (hi[2] - lo[2] > range) { range = hi[2] - lo[2]; ch = 2; }
            return ch;
        }

        static byte[] BuildLookup(List<int> pal)
        {
            var lut = new byte[32768];
            for (int k = 0; k < lut.Length; k++)
            {
                int r = ((k >> 10) & 31) * 8 + 4, g = ((k >> 5) & 31) * 8 + 4, b = (k & 31) * 8 + 4;
                int best = 0, bestD = int.MaxValue;
                for (int i = 0; i < pal.Count; i++)
                {
                    int c = pal[i];
                    int dr = r - (c >> 16 & 255), dg = g - (c >> 8 & 255), db = b - (c & 255);
                    int d = 3 * dr * dr + 4 * dg * dg + 2 * db * db;
                    if (d < bestD) { bestD = d; best = i; }
                }
                lut[k] = (byte)best;
            }
            return lut;
        }

        static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        /// <summary>Palette indices, top row first, with a light 4x4 ordered dither (less banding on skies and
        /// walls, and still the same from frame to frame, so it doesn't shimmer or cost bytes).</summary>
        static void Index(byte[] rgb, int width, int height, byte[] lut, byte[] dst, bool dither)
        {
            for (int y = 0; y < height; y++)
            {
                int src = (height - 1 - y) * width * 3, row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int d = dither ? Bayer[(y & 3) * 4 + (x & 3)] - 8 : 0;   // -8..7, a quarter of a 5-bit step either way
                    int r = Clamp(rgb[src] + d), g = Clamp(rgb[src + 1] + d), b = Clamp(rgb[src + 2] + d);
                    dst[row + x] = lut[Key5(r, g, b)];
                    src += 3;
                }
            }
        }

        static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

        // ---- LZW, variable code size, in 255-byte sub-blocks ----------------------------------------

        static void Lzw(BinaryWriter w, byte[] px, int minCode)
        {
            w.Write((byte)minCode);
            int clear = 1 << minCode, eoi = clear + 1;
            var table = new Dictionary<int, int>(4096);
            int codeSize = minCode + 1, next = eoi + 1;
            var block = new byte[255]; int blockLen = 0;
            int acc = 0, accBits = 0;

            void Emit(int code)
            {
                acc |= code << accBits;
                accBits += codeSize;
                while (accBits >= 8)
                {
                    block[blockLen++] = (byte)acc;
                    acc >>= 8; accBits -= 8;
                    if (blockLen == 255) { w.Write((byte)255); w.Write(block, 0, 255); blockLen = 0; }
                }
            }

            Emit(clear);
            int prefix = px[0];
            for (int i = 1; i < px.Length; i++)
            {
                int c = px[i];
                int key = prefix << 8 | c;
                if (table.TryGetValue(key, out int code)) { prefix = code; continue; }
                Emit(prefix);
                if (next < 4096)
                {
                    table[key] = next++;
                    if (next > (1 << codeSize) && codeSize < 12) codeSize++;
                }
                else
                {
                    Emit(clear);
                    table.Clear();
                    codeSize = minCode + 1; next = eoi + 1;
                }
                prefix = c;
            }
            Emit(prefix);
            Emit(eoi);
            if (accBits > 0) { block[blockLen++] = (byte)acc; if (blockLen == 255) { w.Write((byte)255); w.Write(block, 0, 255); blockLen = 0; } }
            if (blockLen > 0) { w.Write((byte)blockLen); w.Write(block, 0, blockLen); }
            w.Write((byte)0);
        }
    }
}
