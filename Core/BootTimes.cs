using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Kahya.Core
{
    public sealed class BootRecord
    {
        public DateTime Date;
        /// <summary>Masaüstü görünene kadar (ms).</summary>
        public int MainPathMs;
        /// <summary>Arka plandaki açılış işleri de bitene kadar (ms).</summary>
        public int TotalMs;
    }

    public sealed class BootSlowItem
    {
        public DateTime Date;
        /// <summary>app | driver | service</summary>
        public string Kind;
        public string Name;
        /// <summary>Açılışa eklediği gecikme (ms).</summary>
        public int DelayMs;
    }

    /// <summary>
    /// Windows'un açılış ölçümleri (Diagnostics-Performance günlüğü, olay 100) ve açılışı yavaşlatanlar (101/102/103).
    /// Bu günlük yalnızca yöneticiyle okunabildiği için Kahya yönetici olarak açıldığında ve otomatik bakımda
    /// okunup Data klasörüne kaydedilir; normal açılışta kayıttan gösterilir.
    /// </summary>
    public static class BootTimes
    {
        const string LogName = "Microsoft-Windows-Diagnostics-Performance/Operational";
        static string TimesFile => Path.Combine(Settings.Folder, "boot-times.csv");
        static string SlowFile => Path.Combine(Settings.Folder, "boot-slow.csv");
        const int KeepDays = 90;

        /// <summary>Günlükteki son 90 günü okuyup kayda ekler. Yönetici değilse ya da günlük okunamazsa false.</summary>
        public static bool Record()
        {
            if (!Elevation.IsAdmin) return false;
            var times = new List<BootRecord>();
            var slow = new List<BootSlowItem>();
            try
            {
                long ms = KeepDays * 24L * 3600 * 1000;
                var query = new EventLogQuery(LogName, PathType.LogName,
                    $"*[System[(EventID=100 or EventID=101 or EventID=102 or EventID=103) and TimeCreated[timediff(@SystemTime) <= {ms}]]]");
                using (var reader = new EventLogReader(query))
                {
                    for (var e = reader.ReadEvent(); e != null; e = reader.ReadEvent())
                        using (e)
                        {
                            var data = Data(e);
                            var date = e.TimeCreated ?? DateTime.Now;
                            if (e.Id == 100)
                            {
                                int main = Int(data, "MainPathBootTime"), total = Int(data, "BootTime");
                                if (main > 0) times.Add(new BootRecord { Date = date, MainPathMs = main, TotalMs = Math.Max(main, total) });
                            }
                            else
                            {
                                var name = Str(data, "FriendlyName");
                                if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileName(Str(data, "Name") ?? "");
                                int delay = Int(data, "DegradationTime");
                                if (!string.IsNullOrWhiteSpace(name) && delay > 0)
                                    slow.Add(new BootSlowItem { Date = date, Kind = e.Id == 101 ? "app" : e.Id == 102 ? "driver" : "service", Name = name.Trim(), DelayMs = delay });
                            }
                        }
                }
            }
            catch { return false; }

            // Günlük kısa tutulabildiği için eski kayıtlar silinmez, yenileriyle birleştirilir.
            var cutoff = DateTime.Now.AddDays(-KeepDays);
            var allTimes = LoadTimes().Concat(times).Where(t => t.Date >= cutoff)
                .GroupBy(t => t.Date.ToString("s")).Select(g => g.First()).OrderBy(t => t.Date).ToList();
            var allSlow = LoadSlow().Concat(slow).Where(s => s.Date >= cutoff)
                .GroupBy(s => s.Date.ToString("s") + s.Kind + s.Name).Select(g => g.First()).OrderBy(s => s.Date).ToList();
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.WriteAllLines(TimesFile, allTimes.Select(t => string.Join(";", t.Date.ToString("s"), t.MainPathMs, t.TotalMs)));
                File.WriteAllLines(SlowFile, allSlow.Select(s => string.Join(";", s.Date.ToString("s"), s.Kind, s.DelayMs, s.Name.Replace(";", ","))));
            }
            catch { return false; }
            return true;
        }

        public static List<BootRecord> LoadTimes()
        {
            var list = new List<BootRecord>();
            try
            {
                if (!File.Exists(TimesFile)) return list;
                foreach (var line in File.ReadAllLines(TimesFile))
                {
                    var c = line.Split(';');
                    if (c.Length >= 3 && DateTime.TryParse(c[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) &&
                        int.TryParse(c[1], out var main) && int.TryParse(c[2], out var total))
                        list.Add(new BootRecord { Date = d, MainPathMs = main, TotalMs = total });
                }
            }
            catch { }
            return list;
        }

        public static List<BootSlowItem> LoadSlow()
        {
            var list = new List<BootSlowItem>();
            try
            {
                if (!File.Exists(SlowFile)) return list;
                foreach (var line in File.ReadAllLines(SlowFile))
                {
                    var c = line.Split(new[] { ';' }, 4);
                    if (c.Length == 4 && DateTime.TryParse(c[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && int.TryParse(c[2], out var delay))
                        list.Add(new BootSlowItem { Date = d, Kind = c[1], DelayMs = delay, Name = c[3] });
                }
            }
            catch { }
            return list;
        }

        /// <summary>Son 30 günde açılışı en çok geciktirenler (ortalama gecikmeye göre).</summary>
        public static List<(string name, string kind, int avgMs, int count)> Slowest(int top = 5)
        {
            var since = DateTime.Now.AddDays(-30);
            return LoadSlow().Where(s => s.Date >= since)
                .GroupBy(s => s.Kind + "|" + s.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => (g.First().Name, g.First().Kind, (int)g.Average(s => s.DelayMs), g.Count()))
                .OrderByDescending(x => x.Item3).Take(top).ToList();
        }

        static Dictionary<string, string> Data(EventRecord e)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var xml = XElement.Parse(e.ToXml());
                foreach (var d in xml.Descendants().Where(x => x.Name.LocalName == "Data"))
                {
                    var name = (string)d.Attribute("Name");
                    if (name != null) map[name] = d.Value;
                }
            }
            catch { }
            return map;
        }

        static string Str(Dictionary<string, string> data, string key) => data.TryGetValue(key, out var v) ? v : null;
        static int Int(Dictionary<string, string> data, string key) => int.TryParse(Str(data, key), out var v) ? v : 0;
    }
}
