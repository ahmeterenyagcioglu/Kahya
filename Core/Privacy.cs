using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>
    /// Tek bir kayıt defteri değeri: "uygulanmış" hali ve Windows varsayılanı (null = değer yok).
    /// Adı boşsa DWORD değil, anahtarın boş metin varsayılan değeridir (ör. klasik sağ tık menüsü): uygulanınca
    /// anahtar oluşturulur, geri alınınca üst anahtarıyla birlikte silinir.
    /// </summary>
    public sealed class RegSetting
    {
        public RegistryHive Hive;
        public string Path;
        public string Name;
        public int Applied;
        public int? Default;

        public RegSetting(RegistryHive hive, string path, string name, int applied, int? @default)
        {
            Hive = hive; Path = path; Name = name; Applied = applied; Default = @default;
        }

        public bool EmptyDefault => Name.Length == 0;
    }

    public sealed class PrivacyTweak : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        /// <summary>Listede altında durduğu başlık.</summary>
        public string Section { get; set; }
        public bool Recommended { get; set; }
        public bool RestartsExplorer { get; set; }
        public List<RegSetting> Values { get; } = new List<RegSetting>();

        public string Badge => Recommended ? T("önerilen") : T("isteğe bağlı");

        /// <summary>Bilgisayarın tamamını etkileyen (HKLM) bir ayar: yönetici olmadan değiştirilemez.</summary>
        public bool Locked => !Elevation.IsAdmin && Values.Any(v => v.Hive == RegistryHive.LocalMachine);

        bool _isApplied;
        public bool IsApplied
        {
            get => _isApplied;
            set { if (_isApplied != value) { _isApplied = value; Notify(nameof(IsApplied)); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>
    /// Windows'un kendi ayarlarındaki reklam/öneri/telemetri seçenekleri. Sadece kayıt defteri değerleri değişir,
    /// servis kapatılmaz; anahtarı geri almak Windows varsayılanını geri yazar.
    /// </summary>
    public static class Privacy
    {
        const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const RegistryHive U = RegistryHive.CurrentUser;
        const RegistryHive M = RegistryHive.LocalMachine;

        const string SPrivacy = "Gizlilik ve reklamlar";
        const string SWindows = "Görünüm ve kullanım";

        public static List<PrivacyTweak> Load()
        {
            string section = SPrivacy;
            PrivacyTweak Tweak(string id, string name, string description, bool recommended, bool restartsExplorer, params RegSetting[] values)
            {
                var t = new PrivacyTweak
                {
                    Id = id, Name = T(name), Description = T(description), Section = T(section),
                    Recommended = recommended, RestartsExplorer = restartsExplorer,
                };
                t.Values.AddRange(values);
                return t;
            }

            var list = new List<PrivacyTweak>
            {
                Tweak("ad_id", "Reklam kimliğini kapat",
                    "Uygulamalar sana özel reklam gösteremez.", true, false,
                    new RegSetting(U, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1)),

                Tweak("start_suggestions", "Başlat menüsündeki önerileri ve reklamları kapat",
                    "Başlat'taki 'önerilen' uygulama reklamları ve ipuçları gizlenir.", true, true,
                    new RegSetting(U, Cdm, "SystemPaneSuggestionsEnabled", 0, 1),
                    new RegSetting(U, Cdm, "SubscribedContent-338388Enabled", 0, 1),
                    new RegSetting(U, Adv, "Start_IrisRecommendations", 0, null)),

                Tweak("lockscreen", "Kilit ekranındaki ipuçlarını ve reklamları kapat",
                    "Kilit ekranında ipucu ve tanıtım çıkmaz.", true, false,
                    new RegSetting(U, Cdm, "RotatingLockScreenOverlayEnabled", 0, 1),
                    new RegSetting(U, Cdm, "SubscribedContent-338387Enabled", 0, 1)),

                Tweak("tips", "Windows ipucu bildirimlerini kapat",
                    "İpucu bildirimleri ve \"kurulumu bitirelim\" ekranı kapanır.", true, false,
                    new RegSetting(U, Cdm, "SubscribedContent-338389Enabled", 0, 1),
                    new RegSetting(U, Cdm, "SoftLandingEnabled", 0, 1),
                    new RegSetting(U, @"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0, null)),

                Tweak("settings_suggestions", "Ayarlar uygulamasındaki önerileri kapat",
                    "Ayarlar'da gösterilen 'önerilen içerik' kutuları gizlenir.", true, false,
                    new RegSetting(U, Cdm, "SubscribedContent-338393Enabled", 0, 1),
                    new RegSetting(U, Cdm, "SubscribedContent-353694Enabled", 0, 1),
                    new RegSetting(U, Cdm, "SubscribedContent-353696Enabled", 0, 1)),

                Tweak("auto_apps", "Önerilen uygulamaların kendiliğinden yüklenmesini engelle",
                    "Candy Crush gibi uygulamalar kendiliğinden yüklenmez.", true, false,
                    new RegSetting(U, Cdm, "SilentInstalledAppsEnabled", 0, 1),
                    new RegSetting(U, Cdm, "PreInstalledAppsEnabled", 0, 1),
                    new RegSetting(U, Cdm, "OemPreInstalledAppsEnabled", 0, 1)),

                Tweak("web_search", "Başlat aramasındaki Bing web sonuçlarını kapat",
                    "Başlat araması sadece bilgisayarda arar.", true, true,
                    new RegSetting(U, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1, null)),

                Tweak("explorer_ads", "Gezgin'deki OneDrive / Microsoft 365 bildirimlerini kapat",
                    "Gezgin'in üstündeki tanıtımlar gizlenir.", true, true,
                    new RegSetting(U, Adv, "ShowSyncProviderNotifications", 0, null)),

                Tweak("tailored", "Kişiye özel deneyimleri kapat",
                    "Tanılama verileri reklam için kullanılmaz.", true, false,
                    new RegSetting(U, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1)),

                Tweak("feedback", "Geri bildirim isteklerini kapat",
                    "Windows 'bu özellik hakkında ne düşünüyorsun?' diye sormaz.", true, false,
                    new RegSetting(U, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, null)),

                Tweak("telemetry", "Tanılama verilerini 'yalnızca gerekli' seviyesine indir",
                    "Ayarlar'da \"kuruluşunuz yönetiyor\" yazısı çıkabilir, normal.", false, false,
                    new RegSetting(M, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1, null)),

                Tweak("activity", "Etkinlik geçmişini kapat",
                    "Açtığın uygulama ve dosyaların geçmişi tutulmaz.", false, false,
                    new RegSetting(M, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0, null),
                    new RegSetting(M, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0, null)),
            };

            bool win11 = PreFormat.IsWindows11();
            if (win11)
                list.Add(Tweak("recall", "Recall'u kapat",
                    "Copilot+ bilgisayarlarda ekran görüntüsü kaydedilmez.", false, false,
                    new RegSetting(U, @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, null)));

            section = SWindows;
            list.Add(Tweak("file_ext", "Dosya uzantılarını göster",
                "belge.pdf gibi; sahte dosyalar kolay fark edilir.", false, true,
                new RegSetting(U, Adv, "HideFileExt", 0, 1)));
            list.Add(Tweak("hidden_files", "Gizli dosyaları göster",
                "AppData gibi gizli klasörler soluk görünür.", false, true,
                new RegSetting(U, Adv, "Hidden", 1, 2)));
            list.Add(Tweak("this_pc", "Gezgin 'Bu bilgisayar'da açılsın",
                "Gezgin son kullanılanlar yerine disklerle açılır.", false, false,
                new RegSetting(U, Adv, "LaunchTo", 1, null)));
            if (win11)
            {
                list.Add(Tweak("classic_menu", "Klasik sağ tık menüsü",
                    "Sağ tık hemen bütün seçenekleri gösterir.", false, true,
                    new RegSetting(U, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", 1, null)));
                list.Add(Tweak("copilot", "Copilot düğmesini ve kısayolunu kapat",
                    "Görev çubuğu düğmesi ve Win+C kapanır.", false, true,
                    new RegSetting(U, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, null),
                    new RegSetting(U, Adv, "ShowCopilotButton", 0, null)));
                list.Add(Tweak("widgets", "Pencere Öğeleri'ni (hava durumu, haberler) kapat",
                    "Görev çubuğundaki hava durumu ve haber paneli kapanır.", false, true,
                    new RegSetting(M, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0, null)));
                list.Add(Tweak("end_task", "Görev çubuğunda 'Görevi sonlandır'",
                    "Donan programı görev çubuğundan sağ tıkla kapat.", false, false,
                    new RegSetting(U, Adv + @"\TaskbarDeveloperSettings", "TaskbarEndTask", 1, null)));
            }

            foreach (var t in list)
                t.IsApplied = t.Values.All(IsSet);
            return list;
        }

        /// <summary>
        /// Açınca önce değerin o anki hali yedeklenir (ilk değişiklikte, bir kez). Kapatınca yedek varsa o geri yazılır,
        /// yoksa Windows varsayılanı.
        /// </summary>
        public static void Apply(PrivacyTweak t, bool on)
        {
            var backup = Backup.Read();
            foreach (var v in t.Values)
            {
                using (var root = RegistryKey.OpenBaseKey(v.Hive, RegistryView.Registry64))
                {
                    if (on)
                    {
                        if (!backup.ContainsKey(Backup.Key(v)))
                            backup[Backup.Key(v)] = Current(root, v);
                        Write(root, v, v.Applied);
                    }
                    else
                    {
                        int? restore = backup.TryGetValue(Backup.Key(v), out var original) ? original : v.Default;
                        backup.Remove(Backup.Key(v));
                        Write(root, v, restore);
                    }
                }
            }
            Backup.Save(backup);
        }

        /// <summary>Gizlilik sekmesinde yapılan bütün değişiklikleri, yedeklenen ilk hallerine döndürür. Döndürülen değer sayısını verir.</summary>
        public static int RestoreAll()
        {
            var backup = Backup.Read();
            var failed = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            foreach (var pair in backup)
            {
                var v = Backup.Parse(pair.Key);
                if (v == null) continue;
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(v.Hive, RegistryView.Registry64))
                        Write(root, v, pair.Value);
                    count++;
                }
                catch
                {
                    failed[pair.Key] = pair.Value; // ör. yönetici değilken HKLM; yedekte kalsın, sonra tekrar denenir
                }
            }
            Backup.Save(failed);
            if (failed.Count > 0)
                throw new UnauthorizedAccessException(T("{0} değer yönetici izni olmadan değiştirilemedi.", failed.Count));
            return count;
        }

        public static bool HasBackup => Backup.Read().Count > 0;

        static int? Current(RegistryKey root, RegSetting v)
        {
            using (var k = root.OpenSubKey(v.Path))
            {
                if (v.EmptyDefault)
                    return k?.GetValue("") is string s && s.Length == 0 ? 1 : (int?)null;
                return k?.GetValue(v.Name) is int i ? i : (int?)null;
            }
        }

        static void Write(RegistryKey root, RegSetting v, int? value)
        {
            if (v.EmptyDefault)
            {
                if (value.HasValue)
                {
                    using (var k = root.CreateSubKey(v.Path))
                        k.SetValue("", "", RegistryValueKind.String);
                }
                else
                {
                    root.DeleteSubKeyTree(v.Path.Substring(0, v.Path.LastIndexOf('\\')), throwOnMissingSubKey: false);
                }
            }
            else if (value.HasValue)
            {
                using (var k = root.CreateSubKey(v.Path))
                    k.SetValue(v.Name, value.Value, RegistryValueKind.DWord);
            }
            else
            {
                using (var k = root.OpenSubKey(v.Path, writable: true))
                    k?.DeleteValue(v.Name, throwOnMissingValue: false);
            }
        }

        /// <summary>Data\privacy-backup.ini: "HKCU|yol|ad=değer" satırları; değer yoksa "-".</summary>
        static class Backup
        {
            static string FilePath => System.IO.Path.Combine(Settings.Folder, "privacy-backup.ini");

            public static string Key(RegSetting v) => (v.Hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU") + "|" + v.Path + "|" + v.Name;

            public static RegSetting Parse(string key)
            {
                var p = key.Split('|');
                if (p.Length != 3) return null;
                var hive = p[0] == "HKLM" ? RegistryHive.LocalMachine : p[0] == "HKCU" ? RegistryHive.CurrentUser : (RegistryHive?)null;
                return hive == null ? null : new RegSetting(hive.Value, p[1], p[2], 0, null);
            }

            public static Dictionary<string, int?> Read()
            {
                var d = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (!System.IO.File.Exists(FilePath)) return d;
                    foreach (var line in System.IO.File.ReadAllLines(FilePath))
                    {
                        int eq = line.LastIndexOf('=');
                        if (eq <= 0) continue;
                        var value = line.Substring(eq + 1).Trim();
                        d[line.Substring(0, eq)] = int.TryParse(value, out var i) ? i : (int?)null;
                    }
                }
                catch { }
                return d;
            }

            public static void Save(Dictionary<string, int?> d)
            {
                try
                {
                    if (d.Count == 0)
                    {
                        System.IO.File.Delete(FilePath);
                        return;
                    }
                    System.IO.Directory.CreateDirectory(Settings.Folder);
                    System.IO.File.WriteAllLines(FilePath, d.Select(p => p.Key + "=" + (p.Value?.ToString() ?? "-")));
                }
                catch { }
            }
        }

        static bool IsSet(RegSetting v)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(v.Hive, RegistryView.Registry64))
                    return Current(root, v) == v.Applied;
            }
            catch { return false; }
        }

        /// <summary>Başlat menüsü / Gezgin ayarlarının hemen görünmesi için Gezgin'i yeniden başlatır.</summary>
        public static void RestartExplorer()
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            // Windows genelde Gezgin'i kendisi yeniden başlatır. Başlatmadıysa biz başlatırız ama yönetici yetkisi
            // OLMADAN (runas /trustlevel): yönetici olarak açılan bir masaüstü kabuğu istemeyiz.
            for (int i = 0; i < 10 && System.Diagnostics.Process.GetProcessesByName("explorer").Length == 0; i++)
                System.Threading.Thread.Sleep(500);
            if (System.Diagnostics.Process.GetProcessesByName("explorer").Length == 0)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("runas.exe", "/trustlevel:0x20000 explorer.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
        }
    }
}
