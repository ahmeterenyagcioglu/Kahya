using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Kahya.Core
{
    /// <summary>
    /// Format sonrası asistanının yarıda kalan durumu (Data\restore-state.txt): hangi yedek, hangi adımda kalındı, neler seçili,
    /// neler bitti. Kahya kapanır ya da yönetici olarak yeniden başlarsa asistan buradan devam eder; geri yükleme bitince silinir.
    /// Kahya format sonrası yedeğin yanından açıldığı için Data klasörü de oradadır: dosya yeniden başlatmadan sonra da kalır.
    /// </summary>
    public sealed class RestoreState
    {
        public string Backup;
        public int Page;
        /// <summary>Seçili öğeler: "kategori|anahtar" (drivers|NET - …, program|Google.Chrome…).</summary>
        public HashSet<string> Selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Bitmiş birimler (<see cref="RestoreRunner.Unit"/>).</summary>
        public HashSet<string> Done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public DateTime Saved = DateTime.Now;

        static string FilePath => Path.Combine(Settings.Folder, "restore-state.txt");

        /// <summary>Kayıtlı, yarım kalmış bir geri yükleme (yedeği hâlâ yerinde olan); yoksa null.</summary>
        public static RestoreState Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var s = new RestoreState();
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                    switch (key)
                    {
                        case "backup": s.Backup = value; break;
                        case "page": int.TryParse(value, out s.Page); break;
                        case "saved": DateTime.TryParse(value, out s.Saved); break;
                        case "sel": s.Selected.Add(value); break;
                        case "done": s.Done.Add(value); break;
                    }
                }
                return s.Backup != null && File.Exists(s.Backup) ? s : null;
            }
            catch { return null; }
        }

        public void Save()
        {
            try
            {
                Saved = DateTime.Now;
                var lines = new List<string> { "backup=" + Backup, "page=" + Page, "saved=" + Saved.ToString("s") };
                lines.AddRange(Selected.Select(x => "sel=" + x));
                lines.AddRange(Done.Select(x => "done=" + x));
                Directory.CreateDirectory(Settings.Folder);
                File.WriteAllLines(FilePath, lines, new UTF8Encoding(false));
            }
            catch { }
        }

        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }
    }
}
