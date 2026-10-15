using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Program listesindeki bir satır. Source: winget | msstore | boş (elle kurulur).</summary>
    public sealed class ProgramEntry
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string Version { get; set; }
        public string Source { get; set; }

        public bool Installable => Source == "winget" || Source == "msstore";
    }

    /// <summary>
    /// Format öncesi "yüklü programlar" listesi: düz metin, Not Defteri'nde okunur/yazdırılır.
    /// winget'te olanlar kimlikleriyle kaydedilir (format sonrası Kahya tek seferde kurar), olmayanların sadece adı durur.
    /// Satır biçimi: Ad [sekme] Kimlik [sekme] Kaynak [sekme] Sürüm. '#' ile başlayan satırlar açıklamadır.
    /// </summary>
    public static class ProgramList
    {
        public static List<ProgramEntry> Collect(CleanContext ctx)
        {
            var list = Winget.ListInstalled(ctx)
                .Where(p => p.Name.Length > 0 && !IsNoise(p))
                .GroupBy(p => p.Installable ? p.Id : p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            // Store'dan gelen ama winget kaynağı olmayan (MSIX\) uygulamaların kimliği işe yaramaz; adı kalsın.
            foreach (var p in list.Where(p => !p.Installable))
                p.Id = "";

            // winget bazı programları kurulu kayıtla eşleştiremez (kullanıcı kurulumu, Store sürümü…).
            // Adı hazır katalogdaki bir programla eşleşiyorsa onun kimliğini kullan.
            var catalog = AppCatalog.Build();
            var known = new HashSet<string>(list.Where(p => p.Installable).Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var p in list.Where(p => !p.Installable))
            {
                var match = catalog.FirstOrDefault(c => !known.Contains(c.Id) && SameProgram(p.Name, c.Name));
                if (match == null) continue;
                p.Id = match.Id;
                p.Source = "winget";
                known.Add(match.Id);
            }
            return list.OrderByDescending(p => p.Installable).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>"Spotify - Müzik ve Podcast'ler" ~ "Spotify", "Node.js" ~ "Node.js LTS". Kısa adlarda yanlış eşleşmesin diye en az 4 harf.</summary>
        static bool SameProgram(string installed, string catalog)
        {
            string Norm(string s) => Regex.Replace(s.ToLowerInvariant(), @"[\s\-–—:]+", " ").Trim();
            var a = Norm(installed);
            var b = Norm(catalog);
            if (a.Length < 4 || b.Length < 4) return a == b;
            return a == b || a.StartsWith(b + " ") || b.StartsWith(a + " ");
        }

        /// <summary>Windows'la gelen Store bileşenleri, çalışma ortamı paketleri vb.: listede kalabalık eder, yeniden kurulması gerekmez.</summary>
        static bool IsNoise(ProgramEntry p)
        {
            if (p.Installable) return false;
            if (p.Id.StartsWith("MSIX\\Microsoft.", StringComparison.OrdinalIgnoreCase)) return true;
            if (p.Id.StartsWith("MSIX\\MicrosoftWindows.", StringComparison.OrdinalIgnoreCase)) return true;
            if (p.Id.StartsWith("MSIX\\windows.", StringComparison.OrdinalIgnoreCase)) return true;
            // Başka programların kendi kurduğu bağımlılıklar: ayrıca kurmaya gerek yok.
            return Regex.IsMatch(p.Name,
                @"^(Microsoft (Edge|Update Health Tools|Visual C\+\+ \d{4}|\.NET (SDK|Runtime|Host)|ASP\.NET|Windows Application Compatibility|Visual Studio Installer)|" +
                @"Windows (SDK|Software Development Kit)|Update for |vs_|WinAppRuntime|IIS \d|Rockstar Games SDK)", RegexOptions.IgnoreCase);
        }

        public static void Save(string path, List<ProgramEntry> list)
        {
            var sb = new StringBuilder();
            var auto = list.Where(p => p.Installable).ToList();
            var manual = list.Where(p => !p.Installable).ToList();
            sb.AppendLine(T("# Kahya yüklü program listesi"));
            sb.AppendLine(T("# Bilgisayar: {0} · {1:dd.MM.yyyy HH:mm}", Environment.MachineName, DateTime.Now));
            sb.AppendLine(T("# Format sonrası yeniden kurmak için: Kahya › Uygulamalar › Program kur › 'Listeden yükle…'"));
            sb.AppendLine(T("# Sütunlar sekmeyle ayrılır: ad, winget kimliği, kaynak, sürüm. İstemediğin satırı silebilirsin."));
            sb.AppendLine(T("# Sürüm sadece bilgi içindir: winget ile kurulanların her zaman en yeni sürümü kurulur."));
            sb.AppendLine("#");
            sb.AppendLine(T("# ── winget ile tek tıkla kurulabilenler ({0}) ──", auto.Count));
            foreach (var p in auto) sb.AppendLine(Line(p));
            sb.AppendLine("#");
            sb.AppendLine(T("# ── winget'te yok, elle kurulmalı ({0}) ──", manual.Count));
            foreach (var p in manual) sb.AppendLine(Line(p));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        static string Line(ProgramEntry p) =>
            string.Join("\t", Clean(p.Name), p.Installable ? p.Id : "", p.Installable ? p.Source : T("elle"), Clean(p.Version));

        static string Clean(string s) => (s ?? "").Replace('\t', ' ').Trim();

        /// <summary>Kahya listesini ya da "winget export" JSON dosyasını okur.</summary>
        public static List<ProgramEntry> Load(string path)
        {
            var text = File.ReadAllText(path);
            var list = new List<ProgramEntry>();

            if (text.TrimStart().StartsWith("{"))
            {
                // winget export: sadece kimlikler var; ad yerine kimliği gösteririz.
                foreach (Match m in Regex.Matches(text, "\"PackageIdentifier\"\\s*:\\s*\"([^\"]+)\""))
                    list.Add(new ProgramEntry { Name = m.Groups[1].Value, Id = m.Groups[1].Value, Source = "winget" });
                return list;
            }

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                var c = line.Split('\t');
                var source = c.Length > 2 ? c[2].Trim().ToLowerInvariant() : "";
                var e = new ProgramEntry
                {
                    Name = c[0].Trim(),
                    Id = c.Length > 1 ? c[1].Trim() : "",
                    Source = source == "winget" || source == "msstore" ? source : "",
                    Version = c.Length > 3 ? c[3].Trim() : "",
                };
                if (e.Id.Length == 0) e.Source = "";
                if (e.Name.Length > 0) list.Add(e);
            }
            return list;
        }
    }
}
