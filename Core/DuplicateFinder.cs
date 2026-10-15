using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class DupFile : INotifyPropertyChanged
    {
        public string FullPath { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public DupGroup Group { get; set; }

        public string Name => Path.GetFileName(FullPath);
        public string Details => Path.GetDirectoryName(FullPath) + "  ·  " + Modified.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);

        bool _isChecked;
        /// <summary>İşaretli = silinecek (Geri Dönüşüm Kutusu'na).</summary>
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; Notify(nameof(IsChecked)); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public sealed class DupGroup
    {
        public List<DupFile> Files { get; } = new List<DupFile>();
        public long Size { get; set; }
        public long Wasted => Size * (Files.Count - 1);
        public string Header => T("{0} kopya · her biri {1} · boşa giden {2}", Files.Count, Format.Bytes(Size), Format.Bytes(Wasted));
    }

    /// <summary>
    /// İçeriği birebir aynı dosyaları bulur: boyut → (sabit bağlantıları ayıkla) → ilk 64 KB özeti → tam SHA-1.
    /// Bulut yer tutucu dosyaları (OneDrive/Google Drive'da indirilmemiş) okunmaz; okumak indirmeyi tetiklerdi.
    /// Sistem, program ve oyun klasörlerine girilmez (<see cref="SafePaths"/>).
    /// </summary>
    public static class DuplicateFinder
    {
        const uint FILE_ATTRIBUTE_OFFLINE = 0x1000;
        const uint FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x40000;
        const uint FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x400000;

        public static List<DupGroup> Find(string root, long minSize, Action<string> progress, CancellationToken ct)
        {
            // 1) Dosyaları topla
            var files = new List<(string path, long size, DateTime modified)>();
            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(root));
            int dirs = 0;
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var dir = stack.Pop();
                if (++dirs % 500 == 0)
                    progress(T("Dosyalar listeleniyor… {0:N0} klasör, {1:N0} aday dosya", dirs, files.Count));
                FileSystemInfo[] entries;
                try { entries = dir.GetFileSystemInfos(); } catch { continue; }
                foreach (var e in entries)
                {
                    var attrs = (uint)e.Attributes;
                    if (e is DirectoryInfo sub)
                    {
                        if ((e.Attributes & FileAttributes.ReparsePoint) == 0 && !SafePaths.Skip(sub))
                            stack.Push(sub);
                        continue;
                    }
                    if ((attrs & (FILE_ATTRIBUTE_OFFLINE | FILE_ATTRIBUTE_RECALL_ON_OPEN | FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS)) != 0)
                        continue;
                    try
                    {
                        var f = (FileInfo)e;
                        if (f.Length >= minSize)
                            files.Add((f.FullName, f.Length, f.LastWriteTime));
                    }
                    catch { }
                }
            }

            // 2) Aynı boyuttakiler; aynı fiziksel dosyaya giden sabit bağlantılar tek sayılır
            var candidates = new List<List<(string path, long size, DateTime modified)>>();
            foreach (var bySize in files.GroupBy(f => f.size).Where(g => g.Count() > 1))
            {
                var unique = new List<(string, long, DateTime)>();
                var ids = new HashSet<string>();
                foreach (var f in bySize)
                {
                    var id = FileId(f.path);
                    if (id == null || ids.Add(id))
                        unique.Add(f);
                }
                if (unique.Count > 1)
                    candidates.Add(unique);
            }

            // 3) İlk 64 KB, 4) tam içerik
            var groups = new List<DupGroup>();
            int total = candidates.Sum(c => c.Count), done = 0;
            foreach (var sameSize in candidates)
            {
                foreach (var byHead in GroupByHash(sameSize, 64 * 1024, ct, ref done, total, progress))
                {
                    if (byHead.Count < 2) continue;
                    foreach (var byFull in GroupByHash(byHead, long.MaxValue, ct, ref done, 0, null))
                    {
                        if (byFull.Count < 2) continue;
                        var g = new DupGroup { Size = byFull[0].size };
                        foreach (var f in byFull.OrderBy(f => f.modified))
                            g.Files.Add(new DupFile { FullPath = f.path, Size = f.size, Modified = f.modified, Group = g });
                        groups.Add(g);
                    }
                }
            }
            return groups.OrderByDescending(g => g.Wasted).ToList();
        }

        static List<List<(string path, long size, DateTime modified)>> GroupByHash(
            List<(string path, long size, DateTime modified)> files, long bytes, CancellationToken ct,
            ref int done, int total, Action<string> progress)
        {
            var map = new Dictionary<string, List<(string, long, DateTime)>>();
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                if (progress != null && ++done % 20 == 0)
                    progress(T("İçerikler karşılaştırılıyor… {0:N0} / {1:N0}", done, total));
                var h = Hash(f.path, bytes, ct);
                if (h == null) continue;
                if (!map.TryGetValue(h, out var list)) map[h] = list = new List<(string, long, DateTime)>();
                list.Add(f);
            }
            return map.Values.ToList();
        }

        static string Hash(string path, long maxBytes, CancellationToken ct)
        {
            try
            {
                using (var sha = SHA1.Create())
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20))
                {
                    var buffer = new byte[1 << 20];
                    long remaining = maxBytes;
                    int read;
                    while (remaining > 0 && (read = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining))) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        sha.TransformBlock(buffer, 0, read, null, 0);
                        remaining -= read;
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    return Convert.ToBase64String(sha.Hash);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { return null; }
        }

        // ───────────── sabit bağlantı tespiti ─────────────

        [StructLayout(LayoutKind.Sequential)]
        struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
            public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(IntPtr handle, out BY_HANDLE_FILE_INFORMATION info);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);

        /// <summary>Birim seri no + dosya dizini: aynı fiziksel dosyaya giden iki yol aynı kimliği verir.</summary>
        static string FileId(string path)
        {
            var h = CreateFileW(@"\\?\" + path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            if (h == new IntPtr(-1)) return null;
            try
            {
                return GetFileInformationByHandle(h, out var i)
                    ? $"{i.VolumeSerialNumber:X}-{i.FileIndexHigh:X}-{i.FileIndexLow:X}"
                    : null;
            }
            finally { CloseHandle(h); }
        }
    }
}
