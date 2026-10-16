using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>İndirilenler klasöründe uzun süredir değişmemiş bir dosya ya da klasör.</summary>
    public sealed class OldDownload : INotifyPropertyChanged
    {
        public string Path { get; set; }
        public string Name => System.IO.Path.GetFileName(Path);
        public bool IsFolder { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        /// <summary>Kurulum dosyaları, Arşivler, Klasörler, Diğer.</summary>
        public string Category { get; set; }

        public string SizeText => Format.Bytes(Size);
        public string Details => T("{0} önce", Age(DateTime.Now - Modified));

        static string Age(TimeSpan t) =>
            t.TotalDays >= 365 ? T("{0} yıl", (int)(t.TotalDays / 365)) :
            t.TotalDays >= 30 ? T("{0} ay", (int)(t.TotalDays / 30)) : T("{0} gün", (int)t.TotalDays);

        bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// İndirilenler'de belirli bir süredir değişmemiş öğeler. Kurulum dosyaları ve arşivler genelde bir kez kullanılıp
    /// unutulur; onlar varsayılan seçili gelir. Belgeler, resimler ve klasörler kullanıcı seçmedikçe seçilmez.
    /// Silinenler Geri Dönüşüm Kutusu'na gider.
    /// </summary>
    public static class DownloadsFinder
    {
        public static readonly string[] Installers = { ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".iso", ".img" };
        public static readonly string[] Archives = { ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".xz", ".bz2", ".cab" };

        public static string Folder => FormatBackup.KnownFolderPath("Downloads");

        public static string CategoryOf(string path, bool isFolder)
        {
            if (isFolder) return T("Klasörler");
            var ext = System.IO.Path.GetExtension(path);
            if (Installers.Contains(ext, StringComparer.OrdinalIgnoreCase)) return T("Kurulum dosyaları");
            if (Archives.Contains(ext, StringComparer.OrdinalIgnoreCase)) return T("Arşivler");
            return T("Diğer");
        }

        /// <summary>
        /// En üst düzeydeki öğeler; <paramref name="days"/> günden eskiler (0: hepsi). Klasörün tarihi içindeki en yeni
        /// dosyanınkidir: içinde yakında değişmiş bir şey olan klasör "eski" sayılmaz. Yarım indirmeler (.crdownload,
        /// .part) atlanır, tarayıcı onları hâlâ kullanıyor olabilir.
        /// </summary>
        public static List<OldDownload> Find(string folder, int days)
        {
            var result = new List<OldDownload>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return result;
            var cutoff = days > 0 ? DateTime.Now.AddDays(-days) : DateTime.MaxValue;
            var dir = new DirectoryInfo(folder);
            foreach (var entry in dir.EnumerateFileSystemInfos())
            {
                try
                {
                    if ((entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (entry is FileInfo f)
                    {
                        if (f.Extension.Equals(".crdownload", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".part", StringComparison.OrdinalIgnoreCase)
                            || f.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                        var modified = Max(f.LastWriteTime, f.CreationTime);
                        if (modified > cutoff) continue;
                        var item = new OldDownload { Path = f.FullName, Size = f.Length, Modified = modified, Category = CategoryOf(f.FullName, false) };
                        item.IsChecked = item.Category == T("Kurulum dosyaları") || item.Category == T("Arşivler");
                        result.Add(item);
                    }
                    else if (entry is DirectoryInfo d && (d.Attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        var (size, newest) = Measure(d);
                        var modified = Max(newest, d.CreationTime);
                        if (modified > cutoff) continue;
                        result.Add(new OldDownload { Path = d.FullName, IsFolder = true, Size = size, Modified = modified, Category = CategoryOf(d.FullName, true) });
                    }
                }
                catch { } // erişilemeyen öğe atlanır
            }
            return result.OrderByDescending(r => r.Size).ToList();
        }

        static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

        static (long size, DateTime newest) Measure(DirectoryInfo dir)
        {
            long size = 0;
            var newest = dir.LastWriteTime;
            foreach (var f in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    size += f.Length;
                    if (f.LastWriteTime > newest) newest = f.LastWriteTime;
                }
                catch { }
            }
            return (size, newest);
        }

        /// <summary>Geri Dönüşüm Kutusu'na taşır.</summary>
        public static void Recycle(OldDownload item)
        {
            if (item.IsFolder)
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(item.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
    }
}
