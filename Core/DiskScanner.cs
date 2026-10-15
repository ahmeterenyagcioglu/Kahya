using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Kahya.Core
{
    /// <summary>Taranan klasör ağacındaki bir düğüm. Bellek için tam yol saklanmaz, ebeveynlerden üretilir.</summary>
    public sealed class DirNode
    {
        public string Name;
        public DirNode Parent;
        public long Size;
        public long FilesSize;   // sadece bu klasördeki dosyalar
        public int FileCount;
        public List<DirNode> Children;

        public string FullPath => Parent == null ? Name : Path.Combine(Parent.FullPath, Name);
    }

    public sealed class FileHit
    {
        public string Name { get; set; }
        public string Folder { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public string FullPath => Path.Combine(Folder, Name);
    }

    /// <summary>Bir dosya uzantısının toplamı.</summary>
    public sealed class ExtStat
    {
        public long Size;
        public int Count;
    }

    public sealed class DiskScanResult
    {
        public DirNode Root;
        public List<FileHit> LargestFiles;
        /// <summary>Uzantıya göre toplamlar (".mp4" → boyut/adet; uzantısız dosyalar "" altında).</summary>
        public Dictionary<string, ExtStat> Extensions = new Dictionary<string, ExtStat>(StringComparer.OrdinalIgnoreCase);
        public int Folders;
        public int Inaccessible;
        public DateTime ScannedAt = DateTime.Now;
    }

    /// <summary>Tarama sonuçlarını Data\disk-cache altında saklar; aynı sürücü tekrar taranmaz.</summary>
    public static class DiskCache
    {
        const string Magic = "WCDISK2"; // biçim değişince artır; eski kayıtlar yok sayılıp yeniden taranır

        public static string Folder => Path.Combine(Settings.Folder, "disk-cache");

        static string FileFor(string root) =>
            Path.Combine(Folder, root.Substring(0, 1).ToUpperInvariant() + ".bin");

        public static void Save(DiskScanResult r)
        {
            try
            {
                var path = FileFor(r.Root.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var w = new BinaryWriter(File.Create(path), System.Text.Encoding.UTF8))
                {
                    w.Write(Magic);
                    w.Write(r.ScannedAt.ToBinary());
                    w.Write(r.Folders);
                    w.Write(r.Inaccessible);
                    WriteNode(w, r.Root);
                    w.Write(r.LargestFiles.Count);
                    foreach (var f in r.LargestFiles)
                    {
                        w.Write(f.Folder);
                        w.Write(f.Name);
                        w.Write(f.Size);
                        w.Write(f.Modified.ToBinary());
                    }
                    w.Write(r.Extensions.Count);
                    foreach (var e in r.Extensions)
                    {
                        w.Write(e.Key);
                        w.Write(e.Value.Size);
                        w.Write(e.Value.Count);
                    }
                }
            }
            catch { }
        }

        public static DiskScanResult Load(string root)
        {
            try
            {
                var path = FileFor(root);
                if (!File.Exists(path)) return null;
                using (var r = new BinaryReader(File.OpenRead(path), System.Text.Encoding.UTF8))
                {
                    if (r.ReadString() != Magic) return null;
                    var result = new DiskScanResult
                    {
                        ScannedAt = DateTime.FromBinary(r.ReadInt64()),
                        Folders = r.ReadInt32(),
                        Inaccessible = r.ReadInt32(),
                        Root = ReadNode(r, null),
                    };
                    int count = r.ReadInt32();
                    result.LargestFiles = new List<FileHit>(count);
                    for (int i = 0; i < count; i++)
                        result.LargestFiles.Add(new FileHit
                        {
                            Folder = r.ReadString(), Name = r.ReadString(), Size = r.ReadInt64(), Modified = DateTime.FromBinary(r.ReadInt64()),
                        });
                    count = r.ReadInt32();
                    for (int i = 0; i < count; i++)
                        result.Extensions[r.ReadString()] = new ExtStat { Size = r.ReadInt64(), Count = r.ReadInt32() };
                    return result;
                }
            }
            catch { return null; }
        }

        static void WriteNode(BinaryWriter w, DirNode n)
        {
            w.Write(n.Name);
            w.Write(n.FilesSize);
            w.Write(n.FileCount);
            w.Write(n.Children?.Count ?? 0);
            if (n.Children != null)
                foreach (var c in n.Children)
                    WriteNode(w, c);
        }

        static DirNode ReadNode(BinaryReader r, DirNode parent)
        {
            var n = new DirNode { Name = r.ReadString(), Parent = parent, FilesSize = r.ReadInt64(), FileCount = r.ReadInt32() };
            n.Size = n.FilesSize;
            int children = r.ReadInt32();
            if (children > 0)
            {
                n.Children = new List<DirNode>(children);
                for (int i = 0; i < children; i++)
                {
                    var c = ReadNode(r, n);
                    n.Children.Add(c);
                    n.Size += c.Size;
                }
            }
            return n;
        }
    }

    public static class DriveDetector
    {
        /// <summary>
        /// Gerçek bir disk bölümüne bağlı sürücü harfleri ("C:", "D:"…). Google Drive, sanal diskler vb. burada yoktur.
        /// Sorgu başarısız olursa null döner (o durumda tüm sürücüler fiziksel kabul edilir).
        /// </summary>
        public static HashSet<string> PhysicalLetters()
        {
            try
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_LogicalDiskToPartition"))
                using (var results = searcher.Get())
                {
                    foreach (System.Management.ManagementBaseObject mo in results)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(mo["Dependent"] as string ?? "", "DeviceID=\"([A-Za-z]:)\"");
                        if (m.Success)
                            set.Add(m.Groups[1].Value);
                    }
                }
                return set.Count > 0 ? set : null;
            }
            catch { return null; }
        }
    }

    /// <summary>Win32 FindFirstFileEx ile hızlı, salt-okunur disk taraması. Hiçbir şey silmez.</summary>
    public static class DiskScanner
    {
        const int TopFileCount = 1000; // "büyük ve eski" süzgeci için bolca aday

        public static DiskScanResult Scan(string rootPath, Action<int> progress, CancellationToken ct)
        {
            var result = new DiskScanResult();
            var top = new List<FileHit>();
            long threshold = 0;

            var root = new DirNode { Name = rootPath.TrimEnd('\\') + "\\" };
            Walk(root, root.Name);
            root.Children?.Sort((a, b) => b.Size.CompareTo(a.Size));

            result.Root = root;
            result.LargestFiles = top.OrderByDescending(f => f.Size).Take(TopFileCount).ToList();
            return result;

            void Walk(DirNode node, string path)
            {
                ct.ThrowIfCancellationRequested();
                if (++result.Folders % 2000 == 0)
                    progress?.Invoke(result.Folders);

                var handle = FindFirstFileExW(@"\\?\" + Path.Combine(path, "*"), FindExInfoBasic, out var data,
                                              0, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);
                if (handle == INVALID_HANDLE_VALUE)
                {
                    result.Inaccessible++;
                    return;
                }

                try
                {
                    do
                    {
                        var name = data.cFileName;
                        if (name == "." || name == "..") continue;

                        if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
                        {
                            // Junction / sembolik bağlantılar: aynı verinin iki kez sayılmaması için atla.
                            if ((data.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) continue;

                            var child = new DirNode { Name = name, Parent = node };
                            (node.Children ?? (node.Children = new List<DirNode>())).Add(child);
                            Walk(child, Path.Combine(path, name));
                            node.Size += child.Size;
                        }
                        else
                        {
                            long size = ((long)data.nFileSizeHigh << 32) | data.nFileSizeLow;
                            node.FilesSize += size;
                            node.Size += size;
                            node.FileCount++;

                            var dot = name.LastIndexOf('.');
                            var ext = dot > 0 && name.Length - dot <= 12 ? name.Substring(dot) : "";
                            if (!result.Extensions.TryGetValue(ext, out var stat))
                                result.Extensions[ext] = stat = new ExtStat();
                            stat.Size += size;
                            stat.Count++;

                            if (size > threshold)
                            {
                                top.Add(new FileHit
                                {
                                    Name = name, Folder = path, Size = size,
                                    Modified = DateTime.FromFileTime(((long)data.ftLastWriteTime.dwHighDateTime << 32) | (uint)data.ftLastWriteTime.dwLowDateTime),
                                });
                                if (top.Count >= TopFileCount * 2)
                                {
                                    top.Sort((a, b) => b.Size.CompareTo(a.Size));
                                    top.RemoveRange(TopFileCount, top.Count - TopFileCount);
                                    threshold = top[top.Count - 1].Size;
                                }
                            }
                        }
                    } while (FindNextFileW(handle, out data));
                }
                finally
                {
                    FindClose(handle);
                }

                node.Children?.TrimExcess();
            }
        }

        // ───────────── Win32 ─────────────

        const int FindExInfoBasic = 1;
        const int FIND_FIRST_EX_LARGE_FETCH = 2;
        const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
        static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr FindFirstFileExW(string lpFileName, int fInfoLevelId, out WIN32_FIND_DATAW lpFindFileData,
                                              int fSearchOp, IntPtr lpSearchFilter, int dwAdditionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATAW lpFindFileData);

        [DllImport("kernel32.dll")]
        static extern bool FindClose(IntPtr hFindFile);
    }
}
