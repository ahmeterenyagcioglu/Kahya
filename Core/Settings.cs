using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kahya.Core
{
    /// <summary>
    /// Seçimleri settings.ini dosyasında saklar. Tüm veriler (ayarlar, geçmiş, disk önbelleği) exe'nin yanındaki
    /// Data klasöründe durur: program taşınabilir olur, klasör silinince arkada iz kalmaz.
    /// Oraya yazılamıyorsa %APPDATA%\Kahya kullanılır.
    /// </summary>
    public static class Settings
    {
        static readonly string LegacyFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinCleaner");

        public static readonly string Folder = ResolveFolder();

        static string ResolveFolder()
        {
            try
            {
                var exeDir = Path.GetDirectoryName(typeof(Settings).Assembly.Location);
                var data = Path.Combine(exeDir, "Data");
                Directory.CreateDirectory(data);
                var probe = Path.Combine(data, ".write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                MigrateLegacy(data);
                return data;
            }
            catch
            {
                return LegacyFolder;
            }
        }

        /// <summary>Eski sürümlerin %APPDATA% / %LOCALAPPDATA% altındaki verilerini bir kereliğine Data klasörüne taşır.</summary>
        static void MigrateLegacy(string data)
        {
            try
            {
                if (Directory.Exists(LegacyFolder))
                {
                    foreach (var f in Directory.GetFiles(LegacyFolder))
                    {
                        var target = Path.Combine(data, Path.GetFileName(f));
                        if (!File.Exists(target)) File.Move(f, target);
                    }
                    if (!Directory.EnumerateFileSystemEntries(LegacyFolder).Any()) Directory.Delete(LegacyFolder);
                }
                // Disk önbelleği eskiden ayrı yerdeydi; taşımak yerine siliyoruz (tekrar taranır, büyük olabilir).
                var oldLocal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCleaner");
                if (Directory.Exists(oldLocal)) Directory.Delete(oldLocal, true);
            }
            catch { }
        }

        public static string FilePath => Path.Combine(Folder, "settings.ini");

        public static bool SkipRecentFiles { get; set; }

        /// <summary>Güncellemeden önce Sistem Geri Yükleme noktası oluştur.</summary>
        public static bool RestorePointBeforeUpdates { get; set; } = true;

        /// <summary>"tr", "en" veya null (Windows diline göre).</summary>
        public static string Language { get; set; }

        /// <summary>"system" (Windows'a uyar), "dark" ya da "light".</summary>
        public static string Theme { get; set; } = "system";

        /// <summary>Pencere kapatılınca bildirim alanında çalışmaya devam et.</summary>
        public static bool TrayEnabled { get; set; }

        /// <summary>
        /// Çift tıklanınca kendini yönetici olarak yeniden başlatır (Windows bir kez izin sorar). Bildirim alanında
        /// başlarken ve otomatik bakımda yapılmaz: her oturum açılışında izin penceresi çıkmasın.
        /// </summary>
        public static bool AlwaysAdmin { get; set; } = true;

        /// <summary>Yeni Windows'ta asistan önerildi (o kurulumun tarihi): aynı kurulumda bir daha sorulmaz.</summary>
        public static string FreshOffered { get; set; }

        /// <summary>Format yedeğinin kaydedileceği klasör.</summary>
        public static string BackupTarget { get; set; }

        /// <summary>
        /// Tek tıkla / otomatik bakımda temizliğe ek olarak yapılacaklar. İkisi de varsayılan kapalı:
        /// gözetimsiz çalışan haftalık görev, kullanıcı açıkça seçmeden uygulama güncellemesin.
        /// </summary>
        public static bool MaintenanceUpdates { get; set; }
        public static bool MaintenanceDrivers { get; set; }
        public static bool MaintenanceClean { get; set; } = true;
        public static bool MaintenanceOptimize { get; set; }

        /// <summary>Bakımda temizlenecek kategoriler (Temizlik sekmesinin seçiminden bağımsız). null: hiç ayarlanmadı, Temizlik sekmesindekiler kullanılır.</summary>
        public static HashSet<string> MaintenanceItems { get; set; }

        /// <summary>Güncellemesi atlanan uygulamaların winget kimlikleri.</summary>
        public static HashSet<string> IgnoredUpdates { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Otomatik bakım sıklığı: off, daily, weekly, biweekly.</summary>
        public static string ScheduleMode { get; set; } = "weekly";
        public static DayOfWeek ScheduleDay { get; set; } = DayOfWeek.Sunday;
        public static int ScheduleHour { get; set; } = 12;

        /// <summary>Otomatik yedek sıklığı (off, daily, weekly, biweekly), günü ve saati. Hedef: <see cref="BackupTarget"/>.</summary>
        public static string AutoBackupMode { get; set; } = "off";
        public static DayOfWeek AutoBackupDay { get; set; } = DayOfWeek.Sunday;
        public static int AutoBackupHour { get; set; } = 21;
        /// <summary>Otomatik yedeğe giren kişisel klasörler (Desktop, Documents…). null: Masaüstü, Belgeler, Resimler.</summary>
        public static HashSet<string> AutoBackupFolders { get; set; }

        /// <summary>Kullanıcının uyarısını kapattığı ekran kartı sürücüsü sürümü (aynı sürüm için tekrar uyarılmaz).</summary>
        public static string GpuDismissed { get; set; }

        /// <summary>Temizlik listesinde açık duran gruplar (çevrilmemiş adlarıyla). null: hiç ayarlanmadı.</summary>
        public static HashSet<string> ExpandedGroups { get; set; }

        /// <summary>Temizlik listesinde taramada boş çıkan öğeleri gizle.</summary>
        public static bool HideEmptyItems { get; set; } = true;

        /// <summary>Bildirim alanındayken bir uygulamanın önbelleği bu kadar GB'ı geçince haber ver. 0: kapalı.</summary>
        public static int CacheAlertGb { get; set; } = 5;

        static HashSet<string> _selected;

        /// <summary>Dosyayı okur. Dil, katalog oluşturulmadan önce lazım olduğu için öğelerden ayrı yüklenir.</summary>
        public static void Read()
        {
            _selected = null;
            MaintenanceItems = null;
            ExpandedGroups = null;
            AutoBackupFolders = null;
            IgnoredUpdates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var idx = line.IndexOf('=');
                    if (idx < 0) continue;
                    var key = line.Substring(0, idx).Trim();
                    var value = line.Substring(idx + 1).Trim();
                    switch (key)
                    {
                        case "selected": _selected = Split(value); break;
                        case "skipRecent": SkipRecentFiles = value == "1"; break;
                        case "restorePoint": RestorePointBeforeUpdates = value == "1"; break;
                        case "lang": Language = System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-z]{2,3}(-[a-z0-9]{2,8})?$") ? value : null; break;
                        case "theme": Theme = value == "dark" || value == "light" ? value : "system"; break;
                        case "tray": TrayEnabled = value == "1"; break;
                        case "alwaysAdmin": AlwaysAdmin = value == "1"; break;
                        case "freshOffered": FreshOffered = value.Length > 0 ? value : null; break;
                        case "backupTarget": BackupTarget = value.Length > 0 ? value : null; break;
                        case "maintUpdates": MaintenanceUpdates = value == "1"; break;
                        case "maintDrivers": MaintenanceDrivers = value == "1"; break;
                        case "gpuDismissed": GpuDismissed = value.Length > 0 ? value : null; break;
                        case "maintClean": MaintenanceClean = value != "0"; break;
                        case "maintOptimize": MaintenanceOptimize = value == "1"; break;
                        case "maintItems": MaintenanceItems = Split(value); break;
                        case "ignoredUpdates": IgnoredUpdates = new HashSet<string>(Split(value), StringComparer.OrdinalIgnoreCase); break;
                        case "schedule": ScheduleMode = value == "off" || value == "daily" || value == "biweekly" ? value : "weekly"; break;
                        case "scheduleDay": if (Enum.TryParse(value, out DayOfWeek day)) ScheduleDay = day; break;
                        case "expandedGroups": ExpandedGroups = new HashSet<string>(value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)); break;
                        case "hideEmpty": HideEmptyItems = value != "0"; break;
                        case "cacheAlertGb": if (int.TryParse(value, out var gb) && gb >= 0) CacheAlertGb = gb; break;
                        case "autoBackup": AutoBackupMode = value == "daily" || value == "weekly" || value == "biweekly" ? value : "off"; break;
                        case "autoBackupDay": if (Enum.TryParse(value, out DayOfWeek bday)) AutoBackupDay = bday; break;
                        case "autoBackupHour": if (int.TryParse(value, out var bhour) && bhour >= 0 && bhour < 24) AutoBackupHour = bhour; break;
                        case "autoBackupFolders": AutoBackupFolders = new HashSet<string>(Split(value), StringComparer.OrdinalIgnoreCase); break;
                        case "scheduleHour": if (int.TryParse(value, out var hour) && hour >= 0 && hour < 24) ScheduleHour = hour; break;
                    }
                }
            }
            catch { _selected = null; }
        }

        static HashSet<string> Split(string value) =>
            new HashSet<string>(value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()));

        /// <summary>Bakımda temizlenecek kategoriler: ayrıca seçilmişse onlar, yoksa Temizlik sekmesindekiler (kayıt da yoksa önerilenler).</summary>
        public static bool InMaintenance(CleanItem item) =>
            MaintenanceItems?.Contains(item.Id) ?? _selected?.Contains(item.Id) ?? item.Recommended;

        /// <summary>Kayıtlı seçimleri temizlik öğelerine uygular (kayıt yoksa önerilenler).</summary>
        public static void Load(IEnumerable<CleanItem> items)
        {
            foreach (var item in items)
                item.IsChecked = !item.Locked && (_selected?.Contains(item.Id) ?? item.Recommended);
        }

        public static void Save(IEnumerable<CleanItem> items)
        {
            try
            {
                // Kilitli (yönetici gerektiren) öğelerin seçimi yetkisiz açılışta kaybolmasın.
                var old = _selected;
                _selected = new HashSet<string>(items.Where(i => i.Locked ? old?.Contains(i.Id) ?? i.Recommended : i.IsChecked)
                                                     .Select(i => i.Id));
                Directory.CreateDirectory(Folder);
                var lines = new List<string>
                {
                    "selected=" + string.Join(",", _selected),
                    "skipRecent=" + (SkipRecentFiles ? "1" : "0"),
                    "restorePoint=" + (RestorePointBeforeUpdates ? "1" : "0"),
                    "maintUpdates=" + (MaintenanceUpdates ? "1" : "0"),
                    "maintDrivers=" + (MaintenanceDrivers ? "1" : "0"),
                    "maintClean=" + (MaintenanceClean ? "1" : "0"),
                    "maintOptimize=" + (MaintenanceOptimize ? "1" : "0"),
                    "ignoredUpdates=" + string.Join(",", IgnoredUpdates),
                    "schedule=" + ScheduleMode,
                    "scheduleDay=" + ScheduleDay,
                    "scheduleHour=" + ScheduleHour,
                    "theme=" + Theme,
                    "tray=" + (TrayEnabled ? "1" : "0"),
                    "alwaysAdmin=" + (AlwaysAdmin ? "1" : "0"),
                    "hideEmpty=" + (HideEmptyItems ? "1" : "0"),
                    "cacheAlertGb=" + CacheAlertGb,
                    "autoBackup=" + AutoBackupMode,
                    "autoBackupDay=" + AutoBackupDay,
                    "autoBackupHour=" + AutoBackupHour,
                };
                if (AutoBackupFolders != null)
                    lines.Add("autoBackupFolders=" + string.Join(",", AutoBackupFolders));
                // Grup adlarında virgül olabileceği için ayraç '|'.
                if (ExpandedGroups != null)
                    lines.Add("expandedGroups=" + string.Join("|", ExpandedGroups));
                if (MaintenanceItems != null)
                    lines.Add("maintItems=" + string.Join(",", MaintenanceItems));
                if (Language != null)
                    lines.Add("lang=" + Language);
                if (GpuDismissed != null)
                    lines.Add("gpuDismissed=" + GpuDismissed);
                if (BackupTarget != null)
                    lines.Add("backupTarget=" + BackupTarget);
                if (FreshOffered != null)
                    lines.Add("freshOffered=" + FreshOffered);
                File.WriteAllLines(FilePath, lines);
            }
            catch { }
        }
    }
}
