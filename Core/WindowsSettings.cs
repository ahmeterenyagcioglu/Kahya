using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>
    /// Kişisel Windows ayarları: duvar kâğıdı, tema ve renkler, görev çubuğu, Gezgin, fare, klavye düzenleri.
    /// Hepsi bu kullanıcının kayıt değerleri (HKCU); yedekte ayarlar.txt'ye satır satır yazılır, duvar kâğıdı dosya olarak kopyalanır.
    /// Satır biçimi: grup [sekme] anahtar [sekme] değer [sekme] tür [sekme] veri (ikili veri base64).
    /// </summary>
    public static class WindowsSettings
    {
        public const string File = "ayarlar.txt";
        const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        sealed class Group
        {
            public string Id, Name;
            /// <summary>Anahtar ve değer adları; değer listesi null ise anahtardaki bütün değerler (klavye düzenleri gibi).</summary>
            public (string key, string[] values)[] Keys;
        }

        static readonly Group[] Groups =
        {
            new Group { Id = "wallpaper", Name = "Duvar kâğıdı", Keys = new[] { (@"Control Panel\Desktop", new[] { "WallpaperStyle", "TileWallpaper" }) } },
            new Group { Id = "theme", Name = "Tema ve renkler (koyu/açık, vurgu rengi, saydamlık)", Keys = new[]
            {
                (Personalize, new[] { "AppsUseLightTheme", "SystemUsesLightTheme", "EnableTransparency", "ColorPrevalence" }),
                (@"Software\Microsoft\Windows\DWM", new[] { "AccentColor", "ColorPrevalence", "AccentColorInactive" }),
                (@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", new[] { "AccentColorMenu", "StartColorMenu", "AccentPalette" }),
            } },
            new Group { Id = "taskbar", Name = "Görev çubuğu (hizalama, arama kutusu, düğmeler)", Keys = new[]
            {
                (Advanced, new[] { "TaskbarAl", "TaskbarDa", "TaskbarMn", "ShowTaskViewButton", "TaskbarSmallIcons", "TaskbarGlomLevel", "MMTaskbarEnabled" }),
                (@"Software\Microsoft\Windows\CurrentVersion\Search", new[] { "SearchboxTaskbarMode" }),
            } },
            new Group { Id = "explorer", Name = "Gezgin (dosya uzantıları, gizli dosyalar, açılış sayfası)", Keys = new[]
                { (Advanced, new[] { "HideFileExt", "Hidden", "LaunchTo", "NavPaneExpandToCurrentFolder", "SeparateProcess" }) } },
            new Group { Id = "mouse", Name = "Fare (hız, çift tıklama, düğmeler)", Keys = new[]
                { (@"Control Panel\Mouse", new[] { "MouseSensitivity", "MouseSpeed", "MouseThreshold1", "MouseThreshold2", "DoubleClickSpeed", "SwapMouseButtons" }) } },
            new Group { Id = "keyboard", Name = "Klavye düzenleri ve diller", Keys = new[]
                { (@"Keyboard Layout\Preload", (string[])null), (@"Keyboard Layout\Substitutes", (string[])null) } },
        };

        public static string GroupName(string id) => T(Groups.FirstOrDefault(g => g.Id == id)?.Name ?? id);

        // ───────────── yedekle ─────────────

        /// <summary>Ayarları klasöre yazar; yazılan grup sayısını döndürür.</summary>
        /// <summary>Kayıtta değer adı yerine anlaşılır ad (bilinenler için).</summary>
        static string ValueTitle(string name)
        {
            switch (name)
            {
                case "TaskbarDa": return T("Pencere Öğeleri düğmesi");
                case "TaskbarMn": return T("Sohbet düğmesi");
                case "TaskbarAl": return T("Görev çubuğu hizalaması");
                default: return name;
            }
        }

        public static int Save(string folder)
        {
            Directory.CreateDirectory(folder);
            var sb = new StringBuilder();
            sb.AppendLine(T("# Kahya: Windows ayarları — format sonrası asistanıyla geri yüklenir."));
            var saved = new HashSet<string>();
            foreach (var g in Groups)
                foreach (var (path, values) in g.Keys)
                    using (var k = Registry.CurrentUser.OpenSubKey(path))
                    {
                        if (k == null) continue;
                        foreach (var name in values ?? k.GetValueNames())
                        {
                            var v = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                            if (v == null) continue;
                            var kind = k.GetValueKind(name);
                            string data = v is byte[] b ? Convert.ToBase64String(b) : v is string[] arr ? string.Join("\u0001", arr) : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
                            sb.AppendLine(string.Join("\t", g.Id, path, name, kind, data));
                            saved.Add(g.Id);
                        }
                    }

            // Duvar kâğıdının kendisi: seçilen dosya ya da Windows'un kopyası (TranscodedWallpaper).
            var wallpaper = WallpaperFile();
            if (wallpaper != null)
                try
                {
                    var ext = Path.GetExtension(wallpaper);
                    System.IO.File.Copy(wallpaper, Path.Combine(folder, "duvar-kagidi" + (string.IsNullOrEmpty(ext) ? ".jpg" : ext)), true);
                    saved.Add("wallpaper");
                }
                catch { }
            System.IO.File.WriteAllText(Path.Combine(folder, File), sb.ToString(), new UTF8Encoding(false));
            return saved.Count;
        }

        static string WallpaperFile()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                    if (k?.GetValue("WallPaper") is string p && p.Length > 0 && System.IO.File.Exists(p)) return p;
            }
            catch { }
            var transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
            return System.IO.File.Exists(transcoded) ? transcoded : null;
        }

        // ───────────── geri yükle ─────────────

        /// <summary>Yedekteki ayar gruplarının kimlikleri (asistanın seçim listesi için).</summary>
        public static List<string> GroupsIn(string[] lines, bool hasWallpaper)
        {
            var ids = lines.Where(l => !l.StartsWith("#")).Select(l => l.Split('\t')).Where(c => c.Length == 5).Select(c => c[0]).ToList();
            if (hasWallpaper) ids.Add("wallpaper");
            return Groups.Select(g => g.Id).Where(ids.Contains).ToList();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

        /// <summary>
        /// Seçilen grupları geri yazar. Duvar kâğıdı, fare ve tema hemen değişir; görev çubuğu ve klavye düzenleri
        /// oturumu kapatıp açınca tam uygulanır. Geri yüklenen grup sayısını döndürür.
        /// </summary>
        public static int Restore(string folder, ICollection<string> ids, CleanContext ctx)
        {
            int ok = 0;
            var path = Path.Combine(folder, File);
            var lines = System.IO.File.Exists(path) ? System.IO.File.ReadAllLines(path) : new string[0];
            foreach (var g in Groups.Where(g => ids.Contains(g.Id)))
            {
                ctx.Token.ThrowIfCancellationRequested();
                int written = 0;
                // Klavye düzenleri: listenin tamamı yedektekiyle değişir (eskisi kalırsa sıra karışır).
                if (g.Id == "keyboard")
                    foreach (var (key, _) in g.Keys)
                        try { Registry.CurrentUser.DeleteSubKeyTree(key, false); } catch { }
                foreach (var c in lines.Where(l => !l.StartsWith("#")).Select(l => l.Split('\t')).Where(c => c.Length == 5 && c[0] == g.Id))
                    try
                    {
                        if (!Enum.TryParse(c[3], out RegistryValueKind kind)) continue;
                        object value;
                        switch (kind)
                        {
                            // .NET DWORD'ü işaretli okur (vurgu rengi gibi değerler negatif yazılır); iki biçim de kabul edilir.
                            case RegistryValueKind.DWord: value = unchecked((int)long.Parse(c[4], System.Globalization.CultureInfo.InvariantCulture)); break;
                            case RegistryValueKind.QWord: value = long.Parse(c[4]); break;
                            case RegistryValueKind.Binary: value = Convert.FromBase64String(c[4]); break;
                            case RegistryValueKind.MultiString: value = c[4].Split('\u0001'); break;
                            default: value = c[4]; break;
                        }
                        using (var k = Registry.CurrentUser.CreateSubKey(c[1]))
                            k.SetValue(c[2], value, kind);
                        written++;
                    }
                    // Windows 11 bazı değerleri (TaskbarDa: Pencere Öğeleri düğmesi) korumaya aldı, dışarıdan yazılamaz.
                    // Bunlar kullanıcının Ayarlar'dan tek tıkla değiştirebileceği şeyler; İngilizce ham hata yerine bunu söyle.
                    catch (UnauthorizedAccessException) { ctx.Log("   " + T("{0}: Windows bu ayarın değiştirilmesine izin vermiyor; gerekirse Ayarlar'dan elle değiştir.", ValueTitle(c[2]))); }
                    catch (Exception ex) { ctx.Log("   ! " + c[2] + ": " + ex.Message); }

                if (g.Id == "wallpaper")
                {
                    var file = Directory.GetFiles(folder, "duvar-kagidi.*").FirstOrDefault();
                    if (file != null)
                    {
                        // Yedekteki dosya geçici klasörden silineceği için Resimler'e kopyalanır.
                        var dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Kahya - duvar kagidi" + Path.GetExtension(file));
                        System.IO.File.Copy(file, dest, true);
                        if (SystemParametersInfo(0x0014 /* SPI_SETDESKWALLPAPER */, 0, dest, 0x01 | 0x02)) written++;
                    }
                }
                if (g.Id == "mouse")
                    try
                    {
                        using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse"))
                            if (int.TryParse(k?.GetValue("MouseSensitivity") as string, out var speed))
                                SystemParametersInfo(0x0071 /* SPI_SETMOUSESPEED */, 0, (IntPtr)speed, 0x01 | 0x02);
                    }
                    catch { }
                if (written > 0)
                {
                    ok++;
                    ctx.Log("   ✔ " + GroupName(g.Id));
                }
            }
            // Açık programlar (Gezgin, görev çubuğu) yeni ayarları görsün.
            SendMessageTimeout((IntPtr)0xFFFF, 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, "ImmersiveColorSet", 0x0002, 1000, out _);
            SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, "Policy", 0x0002, 1000, out _);
            if (ids.Contains("taskbar") || ids.Contains("keyboard") || ids.Contains("explorer"))
                ctx.Log("   " + T("Görev çubuğu, Gezgin ve klavye ayarları oturumu kapatıp açınca tam uygulanır."));
            return ok;
        }
    }
}
