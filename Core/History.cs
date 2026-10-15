using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class HistoryEntry
    {
        public DateTime Date { get; set; }
        public string Kind { get; set; }      // manual | maintenance | auto
        public long Freed { get; set; }
        public int Updated { get; set; }
        public int Drivers { get; set; }

        public string DateText => Date.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
        public string KindText =>
            Kind == "auto" ? T("Otomatik bakım") :
            Kind == "maintenance" ? T("Tek tıkla bakım") :
            Freed == 0 && Updated > 0 ? T("Güncelleme") :
            Freed == 0 && Drivers > 0 ? T("Sürücüler") : T("Temizlik");

        public string Details
        {
            get
            {
                var parts = new List<string>();
                if (Freed > 0 || (Updated == 0 && Drivers == 0)) parts.Add(T("{0} boşaltıldı", Format.Bytes(Freed)));
                if (Updated > 0) parts.Add(T("{0} uygulama güncellendi", Updated));
                if (Drivers > 0) parts.Add(T("{0} eski sürücü kaldırıldı", Drivers));
                return string.Join(" · ", parts);
            }
        }
    }

    /// <summary>Her temizlik/bakım sonucunu Data\history.csv dosyasına ekler.</summary>
    public static class History
    {
        static string FilePath => Path.Combine(Settings.Folder, "history.csv");

        public static void Add(HistoryEntry e)
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.AppendAllText(FilePath,
                    string.Join(";", e.Date.ToString("o", CultureInfo.InvariantCulture), e.Kind,
                                e.Freed.ToString(CultureInfo.InvariantCulture), e.Updated, e.Drivers) + Environment.NewLine);
            }
            catch { }
        }

        public static List<HistoryEntry> Load()
        {
            var list = new List<HistoryEntry>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var p = line.Split(';');
                    if (p.Length < 5) continue;
                    if (!DateTime.TryParse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)) continue;
                    list.Add(new HistoryEntry
                    {
                        Date = date,
                        Kind = p[1],
                        Freed = long.TryParse(p[2], out var f) ? f : 0,
                        Updated = int.TryParse(p[3], out var u) ? u : 0,
                        Drivers = int.TryParse(p[4], out var d) ? d : 0,
                    });
                }
            }
            catch { }
            return list.OrderByDescending(e => e.Date).ToList();
        }

        /// <summary>"Bu ay 2,3 GB · toplam 12 GB temizlendi" gibi kısa özet.</summary>
        public static string Summary()
        {
            var all = Load();
            if (all.Count == 0) return T("Henüz temizlik yapılmadı");
            var now = DateTime.Now;
            long month = all.Where(e => e.Date.Year == now.Year && e.Date.Month == now.Month).Sum(e => e.Freed);
            long total = all.Sum(e => e.Freed);
            return T("Bu ay {0} · toplam {1} temizlendi ({2} kez)", Format.Bytes(month), Format.Bytes(total), all.Count);
        }
    }
}
