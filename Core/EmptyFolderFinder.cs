using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class EmptyFolder : INotifyPropertyChanged
    {
        public string FullPath { get; set; }
        public int Nested { get; set; }   // içindeki (yine boş) alt klasör sayısı
        public DateTime Modified { get; set; }

        public string Name => FullPath;
        public string Details => (Nested > 0 ? T("{0} boş alt klasörüyle", Nested) + " · " : "") +
                                 T("son değişiklik {0:dd.MM.yyyy}", Modified);

        bool _isChecked = true;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// İçinde hiç dosya olmayan (alt klasörleri de boş olan) klasörleri bulur.
    /// Sistem, program ve oyun klasörlerine hiç girilmez (<see cref="SafePaths"/>).
    /// Gizli+sistem klasörleri ve bağlantılar (junction) hiç boş sayılmaz.
    /// </summary>
    public static class EmptyFolderFinder
    {
        public static List<EmptyFolder> Find(string folder, Action<string> progress, CancellationToken ct)
        {
            var result = new List<EmptyFolder>();
            int scanned = 0;

            // Kök klasörün kendisi listelenmez, sadece içindekiler.
            foreach (var sub in SafeDirs(folder))
                if (!SafePaths.Skip(sub) && Visit(sub, out var n))
                    result.Add(new EmptyFolder { FullPath = sub.FullName, Nested = n, Modified = sub.LastWriteTime });
            return result.OrderBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase).ToList();

            // Klasör tamamen boşsa true döner ve içindeki boş klasör sayısını verir. Listeye sadece en üstteki boş klasör eklenir.
            bool Visit(DirectoryInfo dir, out int nested)
            {
                nested = 0;
                ct.ThrowIfCancellationRequested();
                if (++scanned % 500 == 0)
                    progress?.Invoke(T("{0:N0} klasör tarandı…", scanned));

                var attr = dir.Attributes;
                if ((attr & FileAttributes.ReparsePoint) != 0) return false;
                if ((attr & FileAttributes.Hidden) != 0 && (attr & FileAttributes.System) != 0) return false;
                if (SafePaths.Skip(dir)) return false;

                bool hasFiles;
                try { hasFiles = dir.EnumerateFiles().Any(); }
                catch { return false; } // erişilemiyorsa boş sayma

                var emptyChildren = new List<(DirectoryInfo dir, int nested)>();
                bool allChildrenEmpty = true;
                foreach (var sub in SafeDirs(dir.FullName))
                {
                    if (Visit(sub, out var n)) emptyChildren.Add((sub, n));
                    else allChildrenEmpty = false;
                }

                if (!hasFiles && allChildrenEmpty)
                {
                    nested = emptyChildren.Count + emptyChildren.Sum(c => c.nested);
                    return true;
                }

                // Bu klasör boş değil: boş alt klasörlerini ayrı ayrı listele.
                foreach (var c in emptyChildren)
                    result.Add(new EmptyFolder { FullPath = c.dir.FullName, Nested = c.nested, Modified = c.dir.LastWriteTime });
                return false;
            }
        }

        static IEnumerable<DirectoryInfo> SafeDirs(string path)
        {
            try { return new DirectoryInfo(path).GetDirectories(); }
            catch { return Enumerable.Empty<DirectoryInfo>(); }
        }

        /// <summary>Silmeden hemen önce klasörün hâlâ dosyasız olduğunu doğrular, sonra kaldırır.</summary>
        public static bool Delete(EmptyFolder f)
        {
            var dir = new DirectoryInfo(f.FullPath);
            if (!dir.Exists) return true;
            if (dir.EnumerateFiles("*", SearchOption.AllDirectories).Any())
                return false;
            foreach (var d in dir.EnumerateDirectories("*", SearchOption.AllDirectories).Concat(new[] { dir }))
                if ((d.Attributes & FileAttributes.ReadOnly) != 0)
                    d.Attributes &= ~FileAttributes.ReadOnly;
            dir.Delete(true);
            return true;
        }
    }
}
