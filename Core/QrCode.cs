using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kahya.Core
{
    /// <summary>
    /// Küçük bir QR kodlayıcı (ISO/IEC 18004): bayt kipi, M düzeltme seviyesi, sürüm 1-10 (en fazla 213 bayt).
    /// Wi-Fi paylaşımı için yeterli; harici kütüphane gerektirmez. Modules[y, x] true = koyu.
    /// </summary>
    public sealed class QrCode
    {
        public readonly int Size;
        public readonly bool[,] Modules;
        readonly bool[,] _function;

        // M seviyesi: sürüm başına (veri kod sözcüğü sayısı, blok başına düzeltme sayısı, blok boyutları).
        static readonly (int ecc, int[] blocks)[] TableM =
        {
            (0, null),
            (10, new[] { 16 }), (16, new[] { 28 }), (26, new[] { 44 }), (18, new[] { 32, 32 }), (24, new[] { 43, 43 }),
            (16, new[] { 27, 27, 27, 27 }), (18, new[] { 31, 31, 31, 31 }), (22, new[] { 38, 38, 39, 39 }),
            (22, new[] { 36, 36, 36, 37, 37 }), (26, new[] { 43, 43, 43, 43, 44 }),
        };

        static readonly int[][] Alignment =
        {
            null, new int[0], new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 }, new[] { 6, 34 },
            new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 },
        };

        /// <summary>Metni UTF-8 bayt olarak kodlar; sığmazsa ArgumentException.</summary>
        public static QrCode Encode(string text)
        {
            var data = Encoding.UTF8.GetBytes(text ?? "");
            for (int v = 1; v <= 10; v++)
            {
                int capacityBits = TableM[v].blocks.Sum() * 8;
                int countBits = v <= 9 ? 8 : 16;
                if (4 + countBits + data.Length * 8 <= capacityBits)
                    return new QrCode(v, data);
            }
            throw new ArgumentException("QR için metin çok uzun.");
        }

        QrCode(int version, byte[] data)
        {
            Size = version * 4 + 17;
            Modules = new bool[Size, Size];
            _function = new bool[Size, Size];
            DrawFunctionPatterns(version);
            var codewords = AddEccAndInterleave(version, DataCodewords(version, data));
            DrawCodewords(codewords);

            int best = 0, bestPenalty = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask);
                DrawFormatBits(mask);
                int p = Penalty();
                if (p < bestPenalty) { bestPenalty = p; best = mask; }
                ApplyMask(mask); // XOR geri alır
            }
            ApplyMask(best);
            DrawFormatBits(best);
        }

        // ───────────── sabit desenler ─────────────

        void Set(int x, int y, bool dark)
        {
            Modules[y, x] = dark;
            _function[y, x] = true;
        }

        void DrawFunctionPatterns(int version)
        {
            for (int i = 0; i < Size; i++)
            {
                Set(6, i, i % 2 == 0);
                Set(i, 6, i % 2 == 0);
            }
            DrawFinder(3, 3);
            DrawFinder(Size - 4, 3);
            DrawFinder(3, Size - 4);

            var pos = Alignment[version];
            for (int i = 0; i < pos.Length; i++)
                for (int j = 0; j < pos.Length; j++)
                {
                    bool corner = (i == 0 && j == 0) || (i == 0 && j == pos.Length - 1) || (i == pos.Length - 1 && j == 0);
                    if (!corner) DrawAlignment(pos[i], pos[j]);
                }

            DrawFormatBits(0); // yer ayırır; maske seçilince yeniden yazılır
            if (version >= 7)
            {
                int rem = version;
                for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                int bits = version << 12 | rem;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((bits >> i) & 1) != 0;
                    int a = Size - 11 + i % 3, b = i / 3;
                    Set(a, b, bit);
                    Set(b, a, bit);
                }
            }
        }

        void DrawFinder(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= Size || yy >= Size) continue;
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(xx, yy, dist != 2 && dist != 4);
                }
        }

        void DrawAlignment(int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    Set(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }

        /// <summary>M seviyesi (format bitleri 00) ve maske için 15 bitlik format bilgisi.</summary>
        internal static int FormatBits(int mask)
        {
            int data = 0 << 3 | mask;
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            return (data << 10 | rem) ^ 0x5412;
        }

        void DrawFormatBits(int mask)
        {
            int bits = FormatBits(mask);
            bool Bit(int i) => ((bits >> i) & 1) != 0;
            for (int i = 0; i <= 5; i++) Set(8, i, Bit(i));
            Set(8, 7, Bit(6));
            Set(8, 8, Bit(7));
            Set(7, 8, Bit(8));
            for (int i = 9; i < 15; i++) Set(14 - i, 8, Bit(i));
            for (int i = 0; i < 8; i++) Set(Size - 1 - i, 8, Bit(i));
            for (int i = 8; i < 15; i++) Set(8, Size - 15 + i, Bit(i));
            Set(8, Size - 8, true); // her zaman koyu modül
        }

        // ───────────── veri ─────────────

        static byte[] DataCodewords(int version, byte[] data)
        {
            var bits = new List<bool>();
            void Append(int value, int count) { for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0); }
            Append(0b0100, 4);
            Append(data.Length, version <= 9 ? 8 : 16);
            foreach (var b in data) Append(b, 8);
            int capacity = TableM[version].blocks.Sum() * 8;
            Append(0, Math.Min(4, capacity - bits.Count));
            Append(0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Append(pad, 8);
            var result = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) result[i >> 3] |= (byte)(0x80 >> (i & 7));
            return result;
        }

        static byte[] AddEccAndInterleave(int version, byte[] data)
        {
            var (eccLen, sizes) = TableM[version];
            var divisor = RsDivisor(eccLen);
            var blocks = new List<byte[]>();
            var eccs = new List<byte[]>();
            int k = 0;
            foreach (var n in sizes)
            {
                var block = data.Skip(k).Take(n).ToArray();
                k += n;
                blocks.Add(block);
                eccs.Add(RsRemainder(block, divisor));
            }
            var result = new List<byte>();
            for (int i = 0; i < sizes.Max(); i++)
                foreach (var b in blocks) if (i < b.Length) result.Add(b[i]);
            for (int i = 0; i < eccLen; i++)
                foreach (var e in eccs) result.Add(e[i]);
            return result.ToArray();
        }

        static int Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z;
        }

        static byte[] RsDivisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = (byte)Mul(result[j], root);
                    if (j + 1 < degree) result[j] ^= result[j + 1];
                }
                root = Mul(root, 0x02);
            }
            return result;
        }

        /// <summary>Reed-Solomon düzeltme kod sözcükleri.</summary>
        internal static byte[] RsRemainder(byte[] data, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            foreach (var b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= (byte)Mul(divisor[i], factor);
            }
            return result;
        }

        internal static byte[] RsDivisorFor(int degree) => RsDivisor(degree);

        void DrawCodewords(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < Size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vert : vert;
                        if (!_function[y, x] && i < data.Length * 8)
                        {
                            Modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                            i++;
                        }
                    }
            }
        }

        void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (_function[y, x]) continue;
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (invert) Modules[y, x] = !Modules[y, x];
                }
        }

        /// <summary>Okunabilirlik cezası (standarttaki dört kural); en düşük cezalı maske seçilir.</summary>
        int Penalty()
        {
            int p = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < Size; a++)
                {
                    int run = 1;
                    for (int b = 1; b < Size; b++)
                    {
                        bool cur = pass == 0 ? Modules[a, b] : Modules[b, a];
                        bool prev = pass == 0 ? Modules[a, b - 1] : Modules[b - 1, a];
                        if (cur == prev) { run++; if (run == 5) p += 3; else if (run > 5) p++; }
                        else run = 1;
                    }
                    for (int b = 0; b + 10 < Size; b++)
                    {
                        bool M(int i) => pass == 0 ? Modules[a, b + i] : Modules[b + i, a];
                        bool core = M(0) && !M(1) && M(2) && M(3) && M(4) && !M(5) && M(6);
                        bool core2 = M(4) && !M(5) && M(6) && M(7) && M(8) && !M(9) && M(10);
                        if (core && !M(7) && !M(8) && !M(9) && !M(10)) p += 40;
                        if (core2 && !M(0) && !M(1) && !M(2) && !M(3)) p += 40;
                    }
                }
            for (int y = 0; y + 1 < Size; y++)
                for (int x = 0; x + 1 < Size; x++)
                {
                    bool c = Modules[y, x];
                    if (c == Modules[y, x + 1] && c == Modules[y + 1, x] && c == Modules[y + 1, x + 1]) p += 3;
                }
            int dark = 0;
            foreach (var m in Modules) if (m) dark++;
            int total = Size * Size;
            p += Math.Abs(dark * 20 - total * 10) / total * 10;
            return p;
        }

        // ───────────── Wi-Fi ─────────────

        /// <summary>Telefon kameralarının tanıdığı Wi-Fi metni: WIFI:T:WPA;S:ad;P:şifre;; (özel karakterler kaçışlı).</summary>
        public static string WifiText(string ssid, string password)
        {
            string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace(":", "\\:").Replace("\"", "\\\"");
            return string.IsNullOrEmpty(password)
                ? $"WIFI:T:nopass;S:{Esc(ssid)};;"
                : $"WIFI:T:WPA;S:{Esc(ssid)};P:{Esc(password)};;";
        }
    }
}
