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
        public string Kind { get; set; }      // manual | maintenance | auto | change
        /// <summary>Ayar değişikliği: başlık (ör. "Spotify açılışta başlamayacak") ve geri alma jetonu (<see cref="Core.Undo"/>).</summary>
        public string Category { get; set; }
        public string Title { get; set; }
        public string Undo { get; set; }
        public bool Undone { get; set; }
        public bool CanUndo => !string.IsNullOrEmpty(Undo) && !Undone;
        internal string Stamp;
        public long Freed { get; set; }
        public int Updated { get; set; }
        public int Drivers { get; set; }

        public string DateText => Date.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
        public string KindText =>
            Kind == "change" ? Category :
            Kind == "auto" ? T("Otomatik bakım") :
            Kind == "maintenance" ? T("Tek tıkla bakım") :
            Freed == 0 && Updated > 0 ? T("Güncelleme") :
            Freed == 0 && Drivers > 0 ? T("Sürücüler") : T("Temizlik");

        public string Details
        {
            get
            {
                if (Kind == "change") return Undone ? Title + " · " + T("geri alındı") : Title;
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
                File.AppendAllText(FilePath, Line(e) + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>Geri alınabilir bir ayar değişikliğini kaydeder.</summary>
        public static void AddChange(string category, string title, string undo) =>
            Add(new HistoryEntry { Date = DateTime.Now, Kind = "change", Category = category, Title = title, Undo = undo });

        static string Line(HistoryEntry e) =>
            string.Join(";", e.Date.ToString("o", CultureInfo.InvariantCulture), e.Kind,
                        e.Freed.ToString(CultureInfo.InvariantCulture), e.Updated, e.Drivers,
                        Uri.EscapeDataString(e.Category ?? ""), Uri.EscapeDataString(e.Title ?? ""), Uri.EscapeDataString(e.Undo ?? ""), e.Undone ? "1" : "0");

        /// <summary>Değişikliği geri alındı olarak işaretler (dosyadaki satırı yeniden yazar).</summary>
        public static void MarkUndone(HistoryEntry e)
        {
            try
            {
                var lines = File.ReadAllLines(FilePath);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!lines[i].StartsWith(e.Stamp + ";")) continue;
                    e.Undone = true;
                    lines[i] = Line(e);
                    break;
                }
                File.WriteAllLines(FilePath, lines);
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
                        Stamp = p[0],
                        Kind = p[1],
                        Freed = long.TryParse(p[2], out var f) ? f : 0,
                        Updated = int.TryParse(p[3], out var u) ? u : 0,
                        Drivers = int.TryParse(p[4], out var d) ? d : 0,
                        Category = p.Length > 5 ? Uri.UnescapeDataString(p[5]) : null,
                        Title = p.Length > 6 ? Uri.UnescapeDataString(p[6]) : null,
                        Undo = p.Length > 7 ? Uri.UnescapeDataString(p[7]) : null,
                        Undone = p.Length > 8 && p[8] == "1",
                    });
                }
            }
            catch { }
            return list.OrderByDescending(e => e.Date).ToList();
        }

        /// <summary>"Bu ay 2,3 GB · toplam 12 GB temizlendi" gibi kısa özet.</summary>
        public static string Summary()
        {
            var all = Load().Where(e => e.Kind != "change").ToList();
            if (all.Count == 0) return T("Henüz temizlik yapılmadı");
            var now = DateTime.Now;
            long month = all.Where(e => e.Date.Year == now.Year && e.Date.Month == now.Month).Sum(e => e.Freed);
            long total = all.Sum(e => e.Freed);
            return T("Bu ay {0} · toplam {1} temizlendi ({2} kez)", Format.Bytes(month), Format.Bytes(total), all.Count);
        }
    }
}
