using System.Text;

namespace SimpleSmaller;

/// 只读图片文件头：显示尺寸（按 EXIF 方向转正）、是否带透明。不解码像素，几毫秒一张。
/// 顺带看一眼文件尾：PNG 没有结束块、JPEG 没有结束标记，就是没下载完。
/// 认得 JPEG、PNG、GIF、WebP、BMP、TIFF、AVIF / HEIC（ISOBMFF），自己解析，不靠系统编解码扩展。
internal sealed record ImageHeader(int Width, int Height, bool HasAlpha, ulong Bytes)
{
    /// 读不出返回 null，`problem` 是给用户看的原因。
    public static ImageHeader? Read(string path, out string problem)
    {
        problem = "读不出这张图";
        try
        {
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            var bytes = (ulong)s.Length;
            var head = ReadAt(s, 0, 32);
            Probe? p = null;
            if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8)
            {
                p = Jpeg(s, out var complete);
                if (p != null && !complete) { problem = "文件不完整"; return null; }
            }
            else if (StartsWith(head, 0, [0x89, (byte)'P', (byte)'N', (byte)'G']))
            {
                p = Png(s);
                if (p != null && !PngHasEnd(s)) { problem = "文件不完整"; return null; }
            }
            else if (StartsWith(head, 0, "GIF8"u8)) p = Gif(head);
            else if (StartsWith(head, 0, "RIFF"u8) && StartsWith(head, 8, "WEBP"u8)) p = WebP(s);
            else if (StartsWith(head, 0, "BM"u8)) p = Bmp(s);
            else if (StartsWith(head, 0, "II*\0"u8) || StartsWith(head, 0, "MM\0*"u8)) p = Tiff(s, 0);
            else if (StartsWith(head, 4, "ftyp"u8)) p = Isobmff(s);
            if (p is not { } v || v.Width <= 0 || v.Height <= 0) return null;
            // EXIF 方向 5–8 是转了 90°，宽高对调。
            var (w, h) = v.Orientation is >= 5 and <= 8 ? (v.Height, v.Width) : (v.Width, v.Height);
            return new ImageHeader(w, h, v.HasAlpha, bytes);
        }
        catch
        {
            return null;
        }
    }

    /// 帧数：GIF 数图像块，APNG 看 acTL，WebP 数 ANMF。只用来判断「动图」，数到 2 就停。读不出为 0。
    public static int FrameCount(string path)
    {
        try
        {
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            var head = ReadAt(s, 0, 16);
            if (StartsWith(head, 0, "GIF8"u8)) return GifFrames(s);
            if (StartsWith(head, 0, [0x89, (byte)'P', (byte)'N', (byte)'G'])) return Png(s)?.Frames ?? 0;
            if (StartsWith(head, 0, "RIFF"u8) && StartsWith(head, 8, "WEBP"u8)) return WebP(s)?.Frames ?? 0;
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    private readonly record struct Probe(int Width, int Height, bool HasAlpha, int Orientation = 1, int Frames = 1);

    // MARK: 读字节

    private static byte[] ReadAt(Stream s, long offset, int count)
    {
        if (offset < 0 || offset >= s.Length || count <= 0) return [];
        s.Position = offset;
        var buf = new byte[(int)Math.Min(count, s.Length - offset)];
        s.ReadExactly(buf);
        return buf;
    }

    private static bool StartsWith(byte[] b, int offset, ReadOnlySpan<byte> sig) =>
        b.Length >= offset + sig.Length && b.AsSpan(offset, sig.Length).SequenceEqual(sig);

    private static uint U16(byte[] b, int o, bool le) => le ? (uint)(b[o] | b[o + 1] << 8) : (uint)(b[o] << 8 | b[o + 1]);
    private static uint U32(byte[] b, int o, bool le) =>
        le ? (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24) : (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
    private static uint U24LE(byte[] b, int o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16);

    // MARK: JPEG

    /// 走一遍段落拿尺寸与 EXIF 方向，再在第一个扫描段之后找结束标记（与引擎 `jpeg_has_end` 同一套规则）。
    /// 不能只看文件尾：动态照片在后面接了视频。
    private static Probe? Jpeg(Stream s, out bool complete)
    {
        complete = false;
        int w = 0, h = 0, orientation = 1, components = 0;
        long i = 2;
        var len = s.Length;
        while (i + 4 <= len)
        {
            var m4 = ReadAt(s, i, 4);
            if (m4[0] != 0xFF) break;
            var m = m4[1];
            if (m == 0xFF) { i += 1; continue; }
            if (m == 0xD8 || m is >= 0xD0 and <= 0xD7) { i += 2; continue; }
            var segLen = m4[2] << 8 | m4[3];
            if (m is >= 0xC0 and <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC && w == 0)
            {
                var sof = ReadAt(s, i + 4, 6);
                if (sof.Length == 6)
                {
                    h = (int)U16(sof, 1, false);
                    w = (int)U16(sof, 3, false);
                    components = sof[5];
                }
            }
            else if (m == 0xE1 && orientation == 1)
            {
                var app = ReadAt(s, i + 4, Math.Max(0, segLen - 2));
                if (StartsWith(app, 0, "Exif\0\0"u8))
                    orientation = Tiff(new MemoryStream(app, 6, app.Length - 6), 0)?.Orientation ?? 1;
            }
            if (m == 0xDA)
            {
                complete = ScanForEnd(s, Math.Min(len, i + 2 + segLen));
                break;
            }
            i += 2 + segLen;
        }
        if (w == 0) return null;
        // JPEG 没有透明；四通道是 CMYK。
        _ = components;
        return new Probe(w, h, false, orientation);
    }

    private static bool ScanForEnd(Stream s, long from)
    {
        s.Position = from;
        var buf = new byte[256 * 1024];
        var prevFF = false;
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            for (var k = 0; k < n; k++)
            {
                if (prevFF && buf[k] == 0xD9) return true;
                prevFF = buf[k] == 0xFF;
            }
        }
        return false;
    }

    // MARK: PNG

    private static Probe? Png(Stream s)
    {
        var ihdr = ReadAt(s, 8, 25);
        if (ihdr.Length < 25 || !StartsWith(ihdr, 4, "IHDR"u8)) return null;
        var w = (int)U32(ihdr, 8, false);
        var h = (int)U32(ihdr, 12, false);
        var colorType = ihdr[17];
        var alpha = colorType is 4 or 6;
        var frames = 1;
        // 在第一个 IDAT 之前找 tRNS（调色板透明）与 acTL（APNG）。
        long pos = 8;
        while (pos + 8 <= s.Length)
        {
            var ch = ReadAt(s, pos, 12);
            if (ch.Length < 8) break;
            var clen = U32(ch, 0, false);
            if (StartsWith(ch, 4, "IDAT"u8)) break;
            if (StartsWith(ch, 4, "tRNS"u8)) alpha = true;
            if (StartsWith(ch, 4, "acTL"u8) && ch.Length >= 12) frames = (int)Math.Min(int.MaxValue, U32(ch, 8, false));
            pos += 12L + clen;
        }
        return new Probe(w, h, alpha, 1, frames);
    }

    /// PNG 以 IEND 块结尾，看文件尾就够。
    private static bool PngHasEnd(Stream s)
    {
        var n = (int)Math.Min(s.Length, 4096);
        var tail = ReadAt(s, s.Length - n, n);
        return tail.AsSpan().IndexOf("IEND"u8) >= 0;
    }

    // MARK: GIF

    private static Probe? Gif(byte[] head)
    {
        if (head.Length < 10) return null;
        return new Probe((int)U16(head, 6, true), (int)U16(head, 8, true), false);
    }

    private static int GifFrames(Stream s)
    {
        var b = new BufferedStream(s, 64 * 1024);
        b.Position = 10;
        var flags = b.ReadByte();
        if (flags < 0) return 0;
        b.Position = 13 + ((flags & 0x80) != 0 ? 3 * (1 << ((flags & 7) + 1)) : 0);
        var frames = 0;
        while (true)
        {
            var tag = b.ReadByte();
            if (tag < 0 || tag == 0x3B) break;
            if (tag == 0x21)
            {
                b.ReadByte();
                if (!SkipSubBlocks(b)) break;
            }
            else if (tag == 0x2C)
            {
                if (++frames > 1) break;
                var desc = new byte[9];
                if (b.Read(desc, 0, 9) < 9) break;
                if ((desc[8] & 0x80) != 0) b.Position += 3 * (1 << ((desc[8] & 7) + 1));
                b.ReadByte();
                if (!SkipSubBlocks(b)) break;
            }
            else break;
        }
        return frames;
    }

    private static bool SkipSubBlocks(Stream b)
    {
        while (true)
        {
            var n = b.ReadByte();
            if (n < 0) return false;
            if (n == 0) return true;
            b.Position += n;
        }
    }

    // MARK: WebP

    private static Probe? WebP(Stream s)
    {
        long pos = 12;
        Probe? p = null;
        var anim = false;
        var frames = 0;
        while (pos + 8 <= s.Length)
        {
            var ch = ReadAt(s, pos, 8 + 30);
            if (ch.Length < 8) break;
            var type = Encoding.ASCII.GetString(ch, 0, 4);
            var clen = U32(ch, 4, true);
            switch (type)
            {
                case "VP8X" when ch.Length >= 18:
                    anim = (ch[8] & 0x02) != 0;
                    p = new Probe((int)U24LE(ch, 12) + 1, (int)U24LE(ch, 15) + 1, (ch[8] & 0x10) != 0);
                    if (!anim) return p;
                    break;
                case "VP8 " when p == null && ch.Length >= 18:
                    return new Probe((int)(U16(ch, 14, true) & 0x3FFF), (int)(U16(ch, 16, true) & 0x3FFF), false);
                case "VP8L" when p == null && ch.Length >= 13:
                {
                    var bits = U32(ch, 9, true);
                    return new Probe((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1, (bits >> 28 & 1) != 0);
                }
                case "ANMF":
                    if (++frames > 1) return p is { } v ? v with { Frames = frames } : null;
                    break;
            }
            pos += 8L + clen + (clen & 1);
        }
        return p is { } r ? r with { Frames = anim ? Math.Max(1, frames) : 1 } : null;
    }

    // MARK: BMP

    private static Probe? Bmp(Stream s)
    {
        var b = ReadAt(s, 14, 26);
        if (b.Length < 12) return null;
        var dib = U32(b, 0, true);
        if (dib == 12) return new Probe((int)U16(b, 4, true), (int)U16(b, 6, true), false);
        if (b.Length < 16) return null;
        var w = (int)U32(b, 4, true);
        var h = Math.Abs((int)U32(b, 8, true));
        var bitCount = U16(b, 14, true);
        return new Probe(w, h, bitCount == 32 && dib >= 56);
    }

    // MARK: TIFF（也用于 JPEG 里的 EXIF）

    private static Probe? Tiff(Stream s, long baseOffset)
    {
        var head = ReadAt(s, baseOffset, 8);
        if (head.Length < 8) return null;
        bool le;
        if (head[0] == 'I' && head[1] == 'I') le = true;
        else if (head[0] == 'M' && head[1] == 'M') le = false;
        else return null;
        if (U16(head, 2, le) != 42) return null;
        long ifd = baseOffset + U32(head, 4, le);
        var countBytes = ReadAt(s, ifd, 2);
        if (countBytes.Length < 2) return null;
        var count = (int)U16(countBytes, 0, le);
        var entries = ReadAt(s, ifd + 2, count * 12);
        int w = 0, h = 0, orientation = 1;
        var alpha = false;
        for (var k = 0; k + 12 <= entries.Length; k += 12)
        {
            var tag = U16(entries, k, le);
            var type = U16(entries, k + 2, le);
            var value = type == 3 ? U16(entries, k + 8, le) : U32(entries, k + 8, le);
            switch (tag)
            {
                case 256: w = (int)value; break;
                case 257: h = (int)value; break;
                case 274: orientation = (int)value; break;
                case 338: alpha = value is 1 or 2; break;
            }
        }
        return new Probe(w, h, alpha, orientation);
    }

    // MARK: AVIF / HEIC

    /// 在 meta / iprp / ipco 里找 ispe（尺寸，取最大的那个：网格图的整图比分块大，缩略图更小）、
    /// irot（转 90° 时宽高对调）、auxC（透明通道）。
    private static Probe? Isobmff(Stream s)
    {
        int w = 0, h = 0;
        var rotated = false;
        var alpha = false;
        void Walk(long start, long end, int depth)
        {
            var pos = start;
            while (pos + 8 <= end && depth < 6)
            {
                var hd = ReadAt(s, pos, 16);
                if (hd.Length < 8) return;
                long size = U32(hd, 0, false);
                var type = Encoding.ASCII.GetString(hd, 4, 4);
                long headerLen = 8;
                if (size == 1 && hd.Length >= 16) { size = (long)((ulong)U32(hd, 8, false) << 32 | U32(hd, 12, false)); headerLen = 16; }
                else if (size == 0) size = end - pos;
                if (size < headerLen) return;
                var body = pos + headerLen;
                var boxEnd = Math.Min(end, pos + size);
                switch (type)
                {
                    case "meta": Walk(body + 4, boxEnd, depth + 1); break;
                    case "iprp" or "ipco": Walk(body, boxEnd, depth + 1); break;
                    case "ispe":
                    {
                        var d = ReadAt(s, body, 12);
                        if (d.Length == 12)
                        {
                            int iw = (int)U32(d, 4, false), ih = (int)U32(d, 8, false);
                            if ((long)iw * ih > (long)w * h) { w = iw; h = ih; }
                        }
                        break;
                    }
                    case "irot":
                    {
                        var d = ReadAt(s, body, 1);
                        if (d.Length == 1 && (d[0] & 3) is 1 or 3) rotated = true;
                        break;
                    }
                    case "auxC":
                    {
                        var d = ReadAt(s, body + 4, (int)Math.Min(128, boxEnd - body - 4));
                        var urn = Encoding.ASCII.GetString(d);
                        if (urn.Contains("alpha") || urn.Contains("auxid:1")) alpha = true;
                        break;
                    }
                }
                pos += size;
            }
        }
        Walk(0, s.Length, 0);
        if (w == 0) return null;
        return new Probe(w, h, alpha, rotated ? 6 : 1);
    }
}
