using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>
    /// Format yedeğinin tek dosyası (.zip). Yedeğin ne içerdiği (<see cref="BackupManifest"/>) ZIP'in açıklamasına yazılır:
    /// arşiv açılmadan, dosyanın sonundan okunur (WinRAR / 7-Zip de gösterir). Açıklama kaybolursa diye aynı metin
    /// arşivin içinde <see cref="ManifestEntry"/> olarak da durur.
    /// </summary>
    public static class BackupArchive
    {
        public const string ManifestEntry = "kahya-yedek.txt";

        /// <summary>
        /// Yedek paketinin uzantısı. İçi düz ZIP'tir (.docx gibi): 7-Zip / WinRAR açar, Gezgin için uzantıyı .zip yapmak yeter.
        /// Eski sürümlerin metin halindeki .kahya'sıyla aynı uzantı; hangisi olduğu içeriğinden anlaşılır (<see cref="IsZip"/>).
        /// </summary>
        public const string PackageExtension = ".kahya";

        /// <summary>Dosya ZIP mi (uzantıya değil ilk baytlara bakar: "PK").</summary>
        public static bool IsZip(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    return fs.ReadByte() == 'P' && fs.ReadByte() == 'K';
            }
            catch { return false; }
        }

        // Zaten sıkıştırılmış dosyalar yeniden sıkıştırılmaz: zaman kaybı, boyut kazancı yok.
        static readonly HashSet<string> Stored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".avif", ".mp4", ".mkv", ".mov", ".avi", ".webm", ".m4v",
            ".mp3", ".m4a", ".aac", ".ogg", ".opus", ".flac", ".zip", ".7z", ".rar", ".gz", ".xz", ".bz2", ".zst", ".cab",
            ".jar", ".docx", ".xlsx", ".pptx", ".pdf", ".msi", ".iso", ".nupkg", ".apk",
            // .mca (Minecraft dünyaları) burada değil: içleri sıkıştırılmış olsa da 4 KB'lık boşluklarla dolu, epey küçülür.
        };

        /// <summary>Klasörün içeriğini ZIP'e koyar (klasörün kendisi değil). Okunamayan dosyalar kayda yazılır ve atlanır.</summary>
        public static void Create(string folder, string zipPath, CleanContext ctx)
        {
            var root = folder.TrimEnd('\\') + "\\";
            var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
            long total = Math.Max(1, files.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } })), done = 0;
            int lastPercent = -1;

            if (File.Exists(zipPath)) File.Delete(zipPath);
            // Kodlama verilmez: .NET Framework ancak öyle Türkçe adları UTF-8 yazıp "UTF-8" işaretini koyar
            // (kodlama elle verilirse işaret konmaz, 7-Zip ve Gezgin "Masaüstü"nü bozuk gösterir).
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    ctx.Token.ThrowIfCancellationRequested();
                    var name = file.Substring(root.Length).Replace('\\', '/');
                    try
                    {
                        var level = Stored.Contains(Path.GetExtension(file)) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                        var entry = zip.CreateEntry(name, level);
                        entry.LastWriteTime = File.GetLastWriteTime(file);
                        using (var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var dst = entry.Open())
                            src.CopyTo(dst, 1 << 16);
                        done += new FileInfo(file).Length;
                    }
                    catch (IOException ex) { ctx.Log("   ! " + T("{0} arşive eklenemedi: {1}", name, ex.Message)); }
                    catch (UnauthorizedAccessException ex) { ctx.Log("   ! " + T("{0} arşive eklenemedi: {1}", name, ex.Message)); }

                    int percent = (int)(done * 100 / total);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        ctx.Progress?.Invoke(T("Yedek paketleniyor… %{0}", percent));
                    }
                }
            }
        }

        // ───────────── ZIP açıklaması ─────────────
        // ZIP'in sonunda "merkezi dizin sonu" kaydı (22 bayt + açıklama) durur; son 2 baytı açıklamanın uzunluğudur.

        const uint EndOfCentralDirectory = 0x06054b50;

        /// <summary>
        /// Açıklamaya yazılan kısa özet. ZIP açıklamalarında kodlama belirtilemiyor: 7-Zip / WinRAR Türkçe karakterleri
        /// bozuk gösteriyordu ("yedeÄŸi"). Bu yüzden açıklama sadece ASCII; asıl bilgiler içerideki UTF-8 dosyada.
        /// </summary>
        public static string Summary(BackupManifest m) =>
            Ascii($"Kahya format yedegi | {m.Computer} | {m.Date:yyyy-MM-dd HH:mm} | Kahya {m.Version}\r\n" +
                  "Format sonrasi: yanindaki Kahya.exe'yi ac > Format sonrasi asistani. Ici ZIP; 7-Zip/WinRAR acar.");

        static string Ascii(string s)
        {
            var map = new Dictionary<char, string> { ['ç'] = "c", ['Ç'] = "C", ['ğ'] = "g", ['Ğ'] = "G", ['ı'] = "i", ['İ'] = "I", ['ö'] = "o", ['Ö'] = "O", ['ş'] = "s", ['Ş'] = "S", ['ü'] = "u", ['Ü'] = "U" };
            var sb = new StringBuilder();
            foreach (var c in s ?? "")
                sb.Append(map.TryGetValue(c, out var r) ? r : c < 128 ? c.ToString() : "?");
            return sb.ToString();
        }

        /// <summary>Kapalı bir ZIP'e açıklama yazar (.NET Framework'te bunun hazır yolu yok; kayıt elle güncellenir).</summary>
        public static void WriteComment(string zipPath, string comment)
        {
            var bytes = Encoding.UTF8.GetBytes(comment ?? "");
            if (bytes.Length > ushort.MaxValue) bytes = bytes.Take(ushort.MaxValue).ToArray();
            using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.ReadWrite))
            {
                long eocd = FindEndRecord(fs) ?? throw new InvalidDataException(T("ZIP dosyası bozuk."));
                fs.SetLength(eocd + 22);
                fs.Position = eocd + 20;
                fs.WriteByte((byte)(bytes.Length & 0xFF));
                fs.WriteByte((byte)(bytes.Length >> 8));
                fs.Write(bytes, 0, bytes.Length);
            }
        }

        /// <summary>ZIP'in açıklaması; yoksa ya da ZIP değilse null.</summary>
        public static string ReadComment(string zipPath)
        {
            try
            {
                using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var eocd = FindEndRecord(fs);
                    if (eocd == null) return null;
                    fs.Position = eocd.Value + 20;
                    int length = fs.ReadByte() | (fs.ReadByte() << 8);
                    if (length == 0) return null;
                    var buffer = new byte[length];
                    int read = fs.Read(buffer, 0, length);
                    return Encoding.UTF8.GetString(buffer, 0, read);
                }
            }
            catch { return null; }
        }

        /// <summary>Kaydın başlangıcı: sondan geriye imza aranır, açıklama uzunluğu dosyanın sonuna denk gelmeli.</summary>
        static long? FindEndRecord(FileStream fs)
        {
            long len = fs.Length;
            if (len < 22) return null;
            int window = (int)Math.Min(len, 22 + ushort.MaxValue);
            var buf = new byte[window];
            fs.Position = len - window;
            int read = 0;
            while (read < window) { int n = fs.Read(buf, read, window - read); if (n <= 0) break; read += n; }
            for (int i = window - 22; i >= 0; i--)
            {
                if (BitConverter.ToUInt32(buf, i) != EndOfCentralDirectory) continue;
                int commentLength = BitConverter.ToUInt16(buf, i + 20);
                if (i + 22 + commentLength == window) return len - window + i;
            }
            return null;
        }

        // ───────────── okuma ─────────────

        /// <summary>Yedekteki göreli yol, ZIP girdisi biçiminde (ileri eğik çizgi, sonda '/').</summary>
        public static string Prefix(string relative) => relative.Replace('\\', '/').Trim('/') + "/";

        /// <summary>
        /// Arşivden yalnız <paramref name="relative"/> altındakileri (ya da o dosyayı) <paramref name="dest"/>'e çıkarır.
        /// Arşivin dışına yazmaya çalışan girdiler ("../") atlanır.
        /// </summary>
        public static int ExtractPart(string zipPath, string relative, string dest, CleanContext ctx)
        {
            var prefix = Prefix(relative);
            var file = prefix.TrimEnd('/');
            var root = Path.GetFullPath(dest).TrimEnd('\\') + "\\";
            int count = 0;
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Read, new UTF8Encoding(false)))
            {
                foreach (var e in zip.Entries)
                {
                    ctx.Token.ThrowIfCancellationRequested();
                    if (!(e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || e.FullName.Equals(file, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var target = Path.GetFullPath(Path.Combine(root, e.FullName.Replace('/', '\\')));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                    count++;
                }
            }
            return count;
        }

        /// <summary>Arşivdeki girdi adları ve boyutları (bir kez okunur, sonra sorgulanır).</summary>
        public static List<(string name, long size)> List(string zipPath)
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Read, new UTF8Encoding(false)))
                return zip.Entries.Select(e => (e.FullName, e.Length)).ToList();
        }
    }
}
