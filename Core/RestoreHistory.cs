using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Kahya.Core
{
    /// <summary>
    /// Bir yedekten neyin ne zaman geri yüklendiği (Data\restore-history.txt). Aynı yedek asistanda tekrar açılınca
    /// önceden geri yüklenenler "✓ tarih geri yüklendi" diye işaretsiz gelir; istenirse yine seçilebilir.
    /// Yedek, dosya yolu değil bilgisayar adı ve yedek tarihiyle tanınır: taşınsa da adı değişse de aynı yedektir.
    /// </summary>
    public static class RestoreHistory
    {
        static string FilePath => Path.Combine(Settings.Folder, "restore-history.txt");

        public static string Key(BackupManifest m) => (m.Computer ?? "?") + "@" + m.Date.ToString("s");

        /// <summary>Bu yedekten geri yüklenen birimler ve zamanları.</summary>
        public static Dictionary<string, DateTime> Load(string key)
        {
            var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(FilePath)) return map;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var c = line.Split('\t');
                    if (c.Length == 3 && c[0] == key && DateTime.TryParse(c[2], out var when))
                        map[c[1]] = when;
                }
            }
            catch { }
            return map;
        }

        public static void Add(string key, string unit)
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.AppendAllText(FilePath, string.Join("\t", key, unit, DateTime.Now.ToString("s")) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
