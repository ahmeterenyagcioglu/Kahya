using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Kamera, mikrofon ya da konumu kullanmış bir uygulama.</summary>
    public sealed class AccessRecord
    {
        public string Capability { get; set; }   // webcam | microphone | location
        public string Name { get; set; }
        public string Path { get; set; }         // masaüstü uygulamasının exe yolu; mağaza uygulamasında null
        public string Package { get; set; }      // mağaza uygulamasının paket adı
        public DateTime? LastStart { get; set; }
        public bool InUse { get; set; }
        /// <summary>Mağaza uygulamasının kendi izni (masaüstü uygulamalarının tek tek izni yoktur).</summary>
        public bool? Allowed { get; set; }

        public string LastText => InUse ? T("şu an kullanıyor")
            : LastStart == null ? T("hiç kullanmamış")
            : Ago(LastStart.Value);
        /// <summary>Masaüstü programı: Windows tek tek kapatmaya izin vermez.</summary>
        public string ToggleNote => Package == null ? T("masaüstü") : null;
        public bool CanToggle => Package != null;
        public bool IsAllowed { get => Allowed != false; set => Allowed = value; }

        static string Ago(DateTime d)
        {
            var span = DateTime.Now - d;
            if (span.TotalMinutes < 60) return T("{0} dk önce", Math.Max(1, (int)span.TotalMinutes));
            if (span.TotalHours < 24) return T("{0} saat önce", (int)span.TotalHours);
            if (span.TotalDays < 2) return T("dün {0:HH:mm}", d);
            return d.ToString("dd.MM.yyyy HH:mm");
        }
    }

    /// <summary>
    /// Windows'un "Gizlilik ve güvenlik" kayıtları (CapabilityAccessManager\ConsentStore): hangi uygulama kamerayı,
    /// mikrofonu ya da konumu en son ne zaman kullandı. İzinler bu kullanıcı için (HKCU) değiştirilir; Ayarlar'daki
    /// anahtarların aynısıdır.
    /// </summary>
    public static class DeviceAccess
    {
        public static readonly string[] Capabilities = { "webcam", "microphone", "location" };
        const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

        public static string Title(string capability) =>
            capability == "webcam" ? T("Kamera") : capability == "microphone" ? T("Mikrofon") : T("Konum");

        public static List<AccessRecord> Load(string capability)
        {
            var list = new List<AccessRecord>();
            try
            {
                using (var cap = Registry.CurrentUser.OpenSubKey(Root + @"\" + capability))
                {
                    if (cap == null) return list;
                    foreach (var sub in cap.GetSubKeyNames())
                    {
                        using (var k = cap.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            if (sub == "NonPackaged")
                            {
                                foreach (var exeKey in k.GetSubKeyNames())
                                    using (var e = k.OpenSubKey(exeKey))
                                    {
                                        var path = exeKey.Replace('#', '\\');
                                        var r = Record(capability, e);
                                        r.Path = path;
                                        r.Name = FriendlyExe(path);
                                        list.Add(r);
                                    }
                            }
                            else
                            {
                                var r = Record(capability, k);
                                r.Package = sub;
                                r.Name = FriendlyPackage(sub);
                                r.Allowed = !"Deny".Equals(k.GetValue("Value") as string, StringComparison.OrdinalIgnoreCase);
                                list.Add(r);
                            }
                        }
                    }
                }
            }
            catch { }
            // Aynı program birkaç kez görünebilir (eski sürüm klasörleri): en yeni kullanım kalsın.
            // Kaldırılmış programların kayıtları Windows'ta kalır; listede gösterilmez.
            var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            list = list.Where(r => r.Path != null ? File.Exists(r.Path) : Directory.Exists(Path.Combine(packages, r.Package))).ToList();
            return list.GroupBy(r => r.Package ?? r.Name, StringComparer.OrdinalIgnoreCase)
                       .Select(g => g.OrderByDescending(r => r.InUse).ThenByDescending(r => r.LastStart ?? DateTime.MinValue).First())
                       .OrderByDescending(r => r.InUse).ThenByDescending(r => r.LastStart ?? DateTime.MinValue)
                       .ToList();
        }

        static AccessRecord Record(string capability, RegistryKey k)
        {
            DateTime? Time(string name)
            {
                try { return k.GetValue(name) is long ft && ft > 0 ? DateTime.FromFileTime(ft) : (DateTime?)null; } catch { return null; }
            }
            var start = Time("LastUsedTimeStart");
            var stop = Time("LastUsedTimeStop");
            return new AccessRecord { Capability = capability, LastStart = start, InUse = start != null && (stop == null || stop < start) };
        }

        /// <summary>Uygulamalar bu aygıta erişebilir mi (Ayarlar'daki ana anahtar)?</summary>
        public static bool IsAllowed(string capability, bool desktop = false)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Root + @"\" + capability + (desktop ? @"\NonPackaged" : "")))
                    return !"Deny".Equals(k?.GetValue("Value") as string, StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        /// <summary>
        /// İzni değiştirir. <paramref name="sub"/>: null = bütün uygulamalar, "NonPackaged" = masaüstü uygulamaları,
        /// başka bir değer = o mağaza uygulaması.
        /// </summary>
        public static void SetAllowed(string capability, string sub, bool allowed)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Root + @"\" + capability + (sub != null ? @"\" + sub : "")))
                k.SetValue("Value", allowed ? "Allow" : "Deny", RegistryValueKind.String);
        }

        static string FriendlyExe(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var desc = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
                    if (!string.IsNullOrEmpty(desc) && desc.Length <= 60) return desc;
                }
            }
            catch { }
            return System.IO.Path.GetFileNameWithoutExtension(path);
        }

        static string FriendlyPackage(string package)
        {
            var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Microsoft.WindowsCamera"] = T("Kamera uygulaması"), ["Microsoft.WindowsSoundRecorder"] = T("Ses Kaydedici"),
                ["Microsoft.ScreenSketch"] = T("Ekran Alıntısı Aracı"), ["Microsoft.Windows.Photos"] = T("Fotoğraflar"),
                ["Microsoft.WindowsMaps"] = T("Haritalar"), ["Microsoft.BingWeather"] = T("Hava Durumu"),
                ["MSTeams"] = "Teams", ["MicrosoftTeams"] = "Teams", ["Microsoft.OutlookForWindows"] = "Outlook",
                ["Microsoft.SkypeApp"] = "Skype", ["5319275A.WhatsAppDesktop"] = "WhatsApp", ["OpenAI.ChatGPT-Desktop"] = "ChatGPT",
                ["Claude"] = "Claude", ["Microsoft.Windows.Cortana"] = "Cortana", ["Microsoft.XboxGamingOverlay"] = "Xbox Game Bar",
                ["SpotifyAB.SpotifyMusic"] = "Spotify", ["windows.immersivecontrolpanel"] = T("Windows Ayarlar"), ["Microsoft.GamingServices"] = "Gaming Services", ["Microsoft.StartExperiencesApp"] = T("Başlat menüsü önerileri"), ["Microsoft.GamingApp"] = "Xbox", ["Microsoft.Copilot"] = "Copilot",
            };
            var name = package;
            int us = name.LastIndexOf('_');
            if (us > 0) name = name.Substring(0, us);
            if (known.TryGetValue(name, out var k)) return k;
            int dot = name.IndexOf('.');
            var shortName = dot >= 0 && dot < name.Length - 1 ? name.Substring(dot + 1) : name;
            return shortName.Replace('.', ' ');
        }
    }
}
