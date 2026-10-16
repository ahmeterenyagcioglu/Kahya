using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>
    /// Geri yüklenecek bir yedek: yeni tek dosyalık .zip ya da eski sürümlerin klasör yedeği (.kahya, belki yanında yedek.7z).
    /// Parçalar ihtiyaç oldukça geçici bir klasöre çıkarılır (ZIP'in tamamı açılmaz); Dispose geçici klasörü siler.
    /// </summary>
    public sealed class BackupSource : IDisposable
    {
        public BackupManifest Manifest { get; private set; }
        /// <summary>Kullanıcının seçtiği dosya (.zip ya da .kahya).</summary>
        public string Path { get; private set; }
        public bool IsZip => _zip != null;

        string _zip;                   // yeni biçim: her şey bu ZIP'in içinde
        string _folder;                // eski biçim: yedek klasörü
        string _legacyArchive;         // eski biçim (1.3.x): klasördeki yedek.7z / yedek.zip
        string _legacyExtracted;
        List<(string name, long size)> _entries;
        readonly string _temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Kahya-geri-yukleme-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        public static BackupSource Open(string path)
        {
            var src = new BackupSource { Path = path };
            // Yeni biçim: .kahya ya da .zip, içi ZIP. Eski biçim: metin halindeki .kahya (klasör yedeğinin içindekiler).
            if (BackupArchive.IsZip(path))
            {
                var text = BackupArchive.ReadComment(path);
                src._zip = path;
                src._entries = BackupArchive.List(path);
                // 1.4.0 sonrası açıklama sadece kısa bir ASCII özet; asıl bilgiler içerideki dosyada.
                // (Önceki paketlerde açıklamanın kendisi tam bilgiydi: "[Kahya]" içerir.)
                if (text == null || !text.Contains("[Kahya]"))
                {
                    if (!src._entries.Any(e => e.name.Equals(BackupArchive.ManifestEntry, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException(T("Bu ZIP bir Kahya yedeği değil."));
                    Directory.CreateDirectory(src._temp);
                    BackupArchive.ExtractPart(path, BackupArchive.ManifestEntry, src._temp, new CleanContext(_ => { }, default));
                    text = File.ReadAllText(System.IO.Path.Combine(src._temp, BackupArchive.ManifestEntry));
                }
                src.Manifest = BackupManifest.Parse(text.Replace("\r", "").Split('\n'), null);
            }
            else
            {
                src.Manifest = BackupManifest.Load(path);
                src._folder = src.Manifest.Root;
                if (src.Manifest.Archive != null)
                {
                    src._legacyArchive = System.IO.Path.Combine(src._folder, src.Manifest.Archive);
                    if (!File.Exists(src._legacyArchive))
                        throw new FileNotFoundException(T("Yedeğin arşivi ({0}) .kahya dosyasının yanında bulunamadı. Yedek klasörünü olduğu gibi kopyaladığından emin ol.", src.Manifest.Archive));
                }
            }
            return src;
        }

        /// <summary>Yedekte bu yol (klasör ya da dosya) var mı?</summary>
        public bool Has(string relative)
        {
            if (string.IsNullOrEmpty(relative)) return false;
            if (IsZip)
            {
                var prefix = BackupArchive.Prefix(relative);
                var file = prefix.TrimEnd('/');
                return _entries.Any(e => e.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || e.name.Equals(file, StringComparison.OrdinalIgnoreCase));
            }
            var p = System.IO.Path.Combine(_folder, relative);
            if (File.Exists(p) || Directory.Exists(p)) return true;
            // Eski sıkıştırılmış yedek: içerik arşiv açılmadan görülemez, .kahya'da yazana güvenilir.
            return _legacyArchive != null && _legacyExtracted == null || _legacyExtracted != null && (File.Exists(System.IO.Path.Combine(_legacyExtracted, relative)) || Directory.Exists(System.IO.Path.Combine(_legacyExtracted, relative)));
        }

        /// <summary>Bu yolun altındakilerin toplam boyutu (sadece ZIP yedekte bilinir; değilse -1).</summary>
        public long Size(string relative)
        {
            if (!IsZip || string.IsNullOrEmpty(relative)) return -1;
            var prefix = BackupArchive.Prefix(relative);
            return _entries.Where(e => e.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || e.name.Equals(prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)).Sum(e => e.size);
        }

        /// <summary>Bir klasörün doğrudan alt klasörlerinin adları (ör. sürücü paketleri).</summary>
        public List<string> SubFolders(string relative)
        {
            if (string.IsNullOrEmpty(relative)) return new List<string>();
            if (IsZip)
            {
                var prefix = BackupArchive.Prefix(relative);
                return _entries.Where(e => e.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.name.Substring(prefix.Length)).Where(r => r.Contains('/'))
                    .Select(r => r.Substring(0, r.IndexOf('/'))).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
            }
            var dir = System.IO.Path.Combine(_folder, relative);
            return Directory.Exists(dir) ? Directory.GetDirectories(dir).Select(System.IO.Path.GetFileName).OrderBy(n => n).ToList() : new List<string>();
        }

        /// <summary>Bir klasörün doğrudan içindeki dosyalar (ad, boyut). Eski sıkıştırılmış biçimde arşiv açılır.</summary>
        public List<(string name, long size)> Files(string relative, CleanContext ctx)
        {
            if (string.IsNullOrEmpty(relative)) return new List<(string, long)>();
            if (IsZip)
            {
                var prefix = BackupArchive.Prefix(relative);
                return _entries.Where(e => e.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && e.name.IndexOf('/', prefix.Length) < 0 && e.name.Length > prefix.Length)
                    .Select(e => (e.name.Substring(prefix.Length), e.size)).OrderBy(e => e.Item1).ToList();
            }
            var dir = Local(relative, ctx);
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir).Select(f => (System.IO.Path.GetFileName(f), new FileInfo(f).Length)).OrderBy(f => f.Item1).ToList()
                : new List<(string, long)>();
        }

        /// <summary>Yedekteki küçük bir metin dosyasının satırları (ayarlar.txt, fontlar.txt…); yoksa boş.</summary>
        public string[] ReadLines(string relativeFile, CleanContext ctx)
        {
            try
            {
                if (!Has(relativeFile)) return new string[0];
                var path = Local(relativeFile, ctx);
                return File.Exists(path) ? File.ReadAllLines(path) : new string[0];
            }
            catch { return new string[0]; }
        }

        /// <summary>
        /// Yedekteki yolun bu bilgisayardaki yerini döndürür; ZIP'teyse önce geçici klasöre çıkarır.
        /// Eski biçimde sürücüler .zip/.7z ise onlar da açılır.
        /// </summary>
        public string Local(string relative, CleanContext ctx)
        {
            if (string.IsNullOrEmpty(relative)) return null;
            if (IsZip)
            {
                var dest = System.IO.Path.Combine(_temp, relative.Replace('/', '\\'));
                if (!File.Exists(dest) && !Directory.Exists(dest))
                {
                    Directory.CreateDirectory(_temp);
                    BackupArchive.ExtractPart(_zip, relative, _temp, ctx);
                }
                return dest;
            }

            var path = System.IO.Path.Combine(_folder, relative);
            if (!File.Exists(path) && !Directory.Exists(path) && _legacyArchive != null)
            {
                if (_legacyExtracted == null)
                {
                    _legacyExtracted = System.IO.Path.Combine(_temp, "arsiv");
                    FormatBackup.Extract(_legacyArchive, ctx, _legacyExtracted);
                }
                path = System.IO.Path.Combine(_legacyExtracted, relative);
            }
            // 1.2.x: sürücüler suruculer.zip / suruculer.7z olarak durabilir.
            if (File.Exists(path) && (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)))
                return FormatBackup.Extract(path, ctx, System.IO.Path.Combine(_temp, System.IO.Path.GetFileNameWithoutExtension(path)));
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_temp)) FormatBackup.ForceDelete(_temp);
        }
    }
}
