using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Format yardımcısı › Otomatik yedek: ayarlar ve Görev Zamanlayıcı kaydı. Yedeğin kendisi Core/AutoBackup.cs'te.
    public partial class MainWindow
    {
        bool _autoBackupBusy;

        void InitAutoBackup()
        {
            AutoBackupFolderPanel.Children.Clear();
            foreach (var f in FormatBackup.PersonalFolders())
            {
                var box = new CheckBox
                {
                    Content = f.Name, Tag = f.Key, Margin = new Thickness(0, 0, 16, 6),
                    IsChecked = AutoBackup.FolderKeys.Contains(f.Key),
                    ToolTip = f.Path + (f.InCloud ? " · " + T("bulutla eşitleniyor") : ""),
                };
                box.SetResourceReference(ForegroundProperty, "Text");
                box.Click += AutoBackupFolder_Click;
                AutoBackupFolderPanel.Children.Add(box);
            }
            // Görev elle silinmişse ayar "açık" görünmesin.
            if (Settings.AutoBackupMode != "off")
            {
                try { if (!Scheduler.Exists(Scheduler.BackupTaskName)) Settings.AutoBackupMode = "off"; } catch { }
            }
            InitAutoBackupCloud();
            RefreshAutoBackup();
        }

        System.Collections.Generic.List<CloudFolders.Cloud> _clouds = new System.Collections.Generic.List<CloudFolders.Cloud>();

        /// <summary>Bulut kopyası seçenekleri: Kapalı + bu bilgisayarda bulunan bulut klasörleri. Kullanıcı seçmedikçe kapalı.</summary>
        void InitAutoBackupCloud()
        {
            try { _clouds = CloudFolders.Detect(); } catch { _clouds.Clear(); }
            // Seçili bulut artık bulunamıyorsa da listede kalsın: kullanıcı kapatabilsin.
            if (Settings.AutoBackupCloud != null && !_clouds.Any(c => c.Path.Equals(Settings.AutoBackupCloud, StringComparison.OrdinalIgnoreCase)))
                _clouds.Add(new CloudFolders.Cloud { Name = Path.GetFileName(Settings.AutoBackupCloud), Path = Settings.AutoBackupCloud });

            AutoBackupCloudPanel.Children.Clear();
            var off = new Button { Content = T("Kapalı"), Tag = "", Style = (Style)FindResource("Seg") };
            off.Click += AutoBackupCloud_Click;
            AutoBackupCloudPanel.Children.Add(off);
            foreach (var c in _clouds)
            {
                var b = new Button { Content = c.Name, Tag = c.Path, ToolTip = c.Path, Style = (Style)FindResource("Seg") };
                b.Click += AutoBackupCloud_Click;
                AutoBackupCloudPanel.Children.Add(b);
            }
        }

        void AutoBackupCloud_Click(object sender, RoutedEventArgs e)
        {
            var path = (string)((Button)sender).Tag;
            if (path.Length == 0) path = null;
            if (string.Equals(path, Settings.AutoBackupCloud, StringComparison.OrdinalIgnoreCase)) return;
            if (path != null && !Confirm(T("Her otomatik yedekten sonra paketin bir kopyası buraya konur:\n{0}\n\nBulutta yalnızca en yeni kopya tutulur. Paket Wi-Fi şifrelerini de içerir; bulut hesabında iki adımlı doğrulama açık olsun. Bulutta yerin yetmezse eşitleme durur.\n\nAçılsın mı?",
                                           CloudFolders.BackupFolder(path))))
                return;
            Settings.AutoBackupCloud = path;
            SaveSettings();
            Log(path == null ? T("Bulut kopyası kapatıldı. Buluttaki kopya silinmedi.") : T("Bulut kopyası açıldı: {0}", CloudFolders.BackupFolder(path)));
            RefreshAutoBackup();
        }

        void RefreshAutoBackup()
        {
            MarkSeg(AutoBackupCloudPanel, b => string.Equals((string)b.Tag, Settings.AutoBackupCloud ?? "", StringComparison.OrdinalIgnoreCase));
            if (Settings.AutoBackupCloud != null)
            {
                var copy = Directory.Exists(CloudFolders.BackupFolder(Settings.AutoBackupCloud))
                    ? Directory.GetFiles(CloudFolders.BackupFolder(Settings.AutoBackupCloud), "Kahya-Yedek-*" + BackupArchive.PackageExtension)
                               .Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).FirstOrDefault()
                    : null;
                AutoBackupCloudInfo.Text = copy == null
                    ? T("Sonraki otomatik yedekte kopyalanacak: {0}", CloudFolders.BackupFolder(Settings.AutoBackupCloud))
                    : T("Buluttaki kopya: {0:dd.MM.yyyy HH:mm} · {1}", copy.LastWriteTime, Format.Bytes(copy.Length));
            }
            else
                AutoBackupCloudInfo.Text = _clouds.Count == 0
                    ? T("OneDrive, Google Drive ya da Dropbox bulunamadı.")
                    : T("İstersen her yedeğin bir kopyası bulut klasörüne de konur; bilgisayar çalınsa ya da bozulsa bile yedeğin kalır.");
            MarkSeg(AutoBackupModePanel, b => (string)b.Tag == Settings.AutoBackupMode);
            MarkSeg(AutoBackupDayPanel, b => (string)b.Tag == Settings.AutoBackupDay.ToString());
            MarkSeg(AutoBackupHourPanel, b => (string)b.Tag == Settings.AutoBackupHour.ToString());
            bool on = Settings.AutoBackupMode != "off";
            AutoBackupWhenPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            AutoBackupDayBar.Visibility = Settings.AutoBackupMode == "weekly" || Settings.AutoBackupMode == "biweekly" ? Visibility.Visible : Visibility.Collapsed;

            var parts = new List<string>();
            if (on) parts.Add(Scheduler.Describe(Settings.AutoBackupMode, Settings.AutoBackupDay, Settings.AutoBackupHour) + " · " +
                              T("bilgisayar kapalıysa ilk açılışta alınır"));
            var latest = AutoBackup.Latest(_backupTarget);
            parts.Add(latest == null ? T("Henüz otomatik yedek yok") : T("Son otomatik yedek: {0:dd.MM.yyyy HH:mm}", latest.Value));
            var sys = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            bool onSystem = _backupTarget.StartsWith(sys, StringComparison.OrdinalIgnoreCase);
            if (on && onSystem) parts.Add("⚠ " + T("Yedek C: diskinde; disk bozulursa yedek de gider. Mümkünse USB ya da ikinci bir disk seç."));
            AutoBackupInfo.Text = string.Join(" · ", parts);
            AutoBackupInfo.SetResourceReference(TextBlock.ForegroundProperty, on && onSystem ? "Warn" : "Muted");
        }

        void MarkSeg(Panel panel, Func<Button, bool> active)
        {
            foreach (var b in panel.Children.OfType<Button>()) Highlight(b, active(b));
        }

        void AutoBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            Settings.AutoBackupFolders = new HashSet<string>(
                AutoBackupFolderPanel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag),
                StringComparer.OrdinalIgnoreCase);
            SaveSettings();
        }

        void AutoBackupMode_Click(object sender, RoutedEventArgs e) => ChangeAutoBackup(mode: (string)((Button)sender).Tag);

        void AutoBackupDay_Click(object sender, RoutedEventArgs e) =>
            ChangeAutoBackup(day: (DayOfWeek)Enum.Parse(typeof(DayOfWeek), (string)((Button)sender).Tag));

        void AutoBackupHour_Click(object sender, RoutedEventArgs e) => ChangeAutoBackup(hour: int.Parse((string)((Button)sender).Tag));

        /// <summary>Ayarı değiştirir ve görevi yeniden kaydeder (görev yönetici olarak çalıştığı için kaydetmek yönetici ister).</summary>
        void ChangeAutoBackup(string mode = null, DayOfWeek? day = null, int? hour = null)
        {
            if (!RequireAdmin()) return;
            Settings.AutoBackupMode = mode ?? Settings.AutoBackupMode;
            Settings.AutoBackupDay = day ?? Settings.AutoBackupDay;
            Settings.AutoBackupHour = hour ?? Settings.AutoBackupHour;
            // Görev ayarlardaki konumu kullanır; varsayılan konum hiç kaydedilmemiş olabilir.
            Settings.BackupTarget = _backupTarget;
            try
            {
                if (Settings.AutoBackupMode == "off")
                {
                    if (Scheduler.Exists(Scheduler.BackupTaskName)) Scheduler.Delete(Scheduler.BackupTaskName);
                    Log(T("Otomatik yedek kapalı."));
                }
                else if (Scheduler.Create(Process.GetCurrentProcess().MainModule.FileName, Settings.AutoBackupMode, Settings.AutoBackupDay,
                                          Settings.AutoBackupHour, Scheduler.BackupTaskName, "/backup"))
                    Log(T("Otomatik yedek: {0} → {1}", Scheduler.Describe(Settings.AutoBackupMode, Settings.AutoBackupDay, Settings.AutoBackupHour), _backupTarget));
                else
                {
                    Log("! " + T("Görev oluşturulamadı."));
                    Settings.AutoBackupMode = "off";
                }
            }
            catch (Exception ex)
            {
                Log("! " + T("Zamanlayıcı hatası: {0}", ex.Message));
            }
            SaveSettings();
            RefreshAutoBackup();
        }

        async void AutoBackupNow_Click(object sender, RoutedEventArgs e)
        {
            if (_autoBackupBusy) return;
            _autoBackupBusy = true;
            AutoBackupNowBtn.IsEnabled = false;
            var token = SetBusy(T("yedekleniyor…"));
            ShowLog(true);
            Log("──────────");
            try
            {
                var target = _backupTarget;
                var zip = await Task.Run(() => AutoBackup.Run(target, Context(token)));
                Log("✔ " + T("Otomatik yedek alındı: {0} ({1})", zip, Format.Bytes(new FileInfo(zip).Length)));
            }
            catch (Exception ex)
            {
                Log("! " + T("Otomatik yedek alınamadı: {0}", ex.Message));
                Info(T("Otomatik yedek alınamadı: {0}", ex.Message));
            }
            finally
            {
                ApplyBusy(false);
                _autoBackupBusy = false;
                AutoBackupNowBtn.IsEnabled = true;
                RefreshAutoBackup();
            }
        }
    }
}
