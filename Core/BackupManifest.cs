using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>
    /// Format yedeğinin kökündeki "&lt;bilgisayar&gt;.kahya" dosyası: yedekte ne olduğunu ve nerede durduğunu yazar.
    /// Kahya bu dosyayla her şeyi tek seferde geri yükler. Düz metindir; '#' ile başlayan satırlar Not Defteri'nde
    /// okuyan biri için açıklamadır, Kahya onları atlar.
    /// </summary>
    public sealed class BackupManifest
    {
        public const string Extension = ".kahya";

        public string Computer = Environment.MachineName;
        public DateTime Date = DateTime.Now;
        public string Version = AppInfo.Version;

        /// <summary>Yedek klasörüne göre yollar. Boşsa o parça yedekte yok.</summary>
        public string Programs, Wifi, ProductKey, Drivers, Fonts, Settings, Games, Bookmarks;

        /// <summary>Kişisel klasörler: anahtar (Desktop, Documents…) → yedekteki yol.</summary>
        public Dictionary<string, string> Folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Okunurken: .kahya dosyasının bulunduğu klasör (yedeğin kökü).</summary>
        public string Root { get; private set; }

        public string Full(string relative) => string.IsNullOrEmpty(relative) || Root == null ? null : Path.Combine(Root, relative);

        public bool IsEmpty => new[] { Programs, Wifi, ProductKey, Drivers, Fonts, Settings, Games, Bookmarks }.All(string.IsNullOrEmpty) && Folders.Count == 0;

        public static string FileName(string computer) => FormatBackup.Safe(computer) + Extension;

        public void Save(string folder, string readMe)
        {
            var sb = new StringBuilder();
            foreach (var line in readMe.Replace("\r", "").TrimEnd().Split('\n'))
                sb.AppendLine("# " + line);
            sb.AppendLine();
            sb.AppendLine("[Kahya]");
            sb.AppendLine("Computer=" + Computer);
            sb.AppendLine("Date=" + Date.ToString("s"));
            sb.AppendLine("Version=" + Version);
            sb.AppendLine();
            sb.AppendLine("[Contents]");
            void W(string key, string value) { if (!string.IsNullOrEmpty(value)) sb.AppendLine(key + "=" + value); }
            W("Programs", Programs);
            W("Wifi", Wifi);
            W("ProductKey", ProductKey);
            W("Drivers", Drivers);
            W("Fonts", Fonts);
            W("Settings", Settings);
            W("Games", Games);
            W("Bookmarks", Bookmarks);
            if (Folders.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[Folders]");
                foreach (var f in Folders) sb.AppendLine(f.Key + "=" + f.Value);
            }
            File.WriteAllText(Path.Combine(folder, FileName(Computer)), sb.ToString(), new UTF8Encoding(true));
        }

        public static BackupManifest Load(string path)
        {
            var m = new BackupManifest { Root = Path.GetDirectoryName(Path.GetFullPath(path)), Computer = null, Version = null };
            string section = "";
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Trim('[', ']'); continue; }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                // Yedekten dışarı çıkan yollar (..\, C:\) kabul edilmez.
                bool safe = !Path.IsPathRooted(value) && !value.Split('\\', '/').Contains("..");

                if (section == "Kahya")
                {
                    if (key == "Computer") m.Computer = value;
                    else if (key == "Date" && DateTime.TryParse(value, out var d)) m.Date = d;
                    else if (key == "Version") m.Version = value;
                }
                else if (section == "Folders" && safe) m.Folders[key] = value;
                else if (section == "Contents" && safe)
                {
                    switch (key)
                    {
                        case "Programs": m.Programs = value; break;
                        case "Wifi": m.Wifi = value; break;
                        case "ProductKey": m.ProductKey = value; break;
                        case "Drivers": m.Drivers = value; break;
                        case "Fonts": m.Fonts = value; break;
                        case "Settings": m.Settings = value; break;
                        case "Games": m.Games = value; break;
                        case "Bookmarks": m.Bookmarks = value; break;
                    }
                }
            }
            if (m.Computer == null && m.IsEmpty)
                throw new InvalidDataException(T("Bu bir Kahya yedek dosyası değil."));
            return m;
        }
    }
}
