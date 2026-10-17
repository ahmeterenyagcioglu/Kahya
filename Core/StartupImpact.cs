using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>
    /// Görev Yöneticisi'nin "Başlangıç etkisi" sütunu: Windows her açılışta, oturum açınca başlayan programların
    /// ne kadar işlemci süresi ve disk okuması harcadığını wdi\LogFiles\StartupInfo altına XML olarak yazar
    /// (son birkaç açılış). Klasör yalnızca yöneticiyle okunabilir.
    /// </summary>
    public static class StartupImpact
    {
        public sealed class Usage
        {
            public int CpuMs;
            public long DiskBytes;
            public int Boots;
        }

        static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"wdi\LogFiles\StartupInfo");

        /// <summary>Program dosya adı (ör. "spotify.exe") → açılış başına ortalama kullanım. Okunamazsa boş.</summary>
        public static Dictionary<string, Usage> Read()
        {
            var result = new Dictionary<string, Usage>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!Directory.Exists(Folder)) return result;
                var files = Directory.GetFiles(Folder, "*.xml");
                var boots = new List<List<(string exe, int cpu, long disk)>>();
                foreach (var f in files)
                {
                    try { boots.Add(Parse(File.ReadAllText(f))); } catch { }
                }
                return Average(boots);
            }
            catch { return result; }
        }

        internal static Dictionary<string, Usage> Average(List<List<(string exe, int cpu, long disk)>> boots)
        {
            var result = new Dictionary<string, Usage>(StringComparer.OrdinalIgnoreCase);
            foreach (var boot in boots)
            {
                // Aynı program bir açılışta birkaç süreç başlatabilir; açılış başına toplanır.
                foreach (var g in boot.GroupBy(p => p.exe, StringComparer.OrdinalIgnoreCase))
                {
                    if (!result.TryGetValue(g.Key, out var u)) result[g.Key] = u = new Usage();
                    u.CpuMs += g.Sum(p => p.cpu);
                    u.DiskBytes += g.Sum(p => p.disk);
                    u.Boots++;
                }
            }
            foreach (var u in result.Values)
            {
                u.CpuMs /= u.Boots;
                u.DiskBytes /= u.Boots;
            }
            return result;
        }

        /// <summary>Bir StartupInfo dosyasındaki süreçler: dosya adı, işlemci (ms), disk (bayt).</summary>
        internal static List<(string exe, int cpu, long disk)> Parse(string xml)
        {
            var list = new List<(string, int, long)>();
            var doc = XDocument.Parse(xml);
            foreach (var p in doc.Descendants().Where(e => e.Name.LocalName == "Process"))
            {
                var name = (string)p.Attribute("Name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                string Child(string n) => p.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(n, StringComparison.OrdinalIgnoreCase))?.Value;
                int.TryParse(Child("CpuUsage"), out var cpu);
                long.TryParse(Child("DiskUsage"), out var disk);
                list.Add((Path.GetFileName(name.Trim()), cpu, disk));
            }
            return list;
        }

        /// <summary>Görev Yöneticisi'nin eşikleri: high | medium | low.</summary>
        public static string Level(Usage u)
        {
            if (u.CpuMs > 1000 || u.DiskBytes > 3L * 1024 * 1024) return "high";
            if (u.CpuMs > 300 || u.DiskBytes > 300L * 1024) return "medium";
            return "low";
        }

        public static string Text(string level) =>
            level == "high" ? T("Yüksek etki") : level == "medium" ? T("Orta etki") : level == "low" ? T("Düşük etki") : null;
    }
}
