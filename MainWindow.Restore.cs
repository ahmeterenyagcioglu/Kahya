using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Kahya.Core;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Format sonrası asistanı (RestoreWizard) buradan açılır.
    public partial class MainWindow
    {
        /// <summary>Kahya zaten açıkken çift tıklanan eski .kahya dosyası buraya yazılır; açık olan kopya okuyup asistanı açar.</summary>
        public static string PendingRestoreFile => Path.Combine(Settings.Folder, "pending-restore.txt");

        RestoreWizard _wizard;

        void RestoreFromBackup_Click(object sender, RoutedEventArgs e) => OpenRestoreWizard(null);

        async void BrowseBackup_Click(object sender, RoutedEventArgs e) => await BackupBrowser.OpenAsync(this);

        /// <summary>Asistanı açar (açıksa öne getirir). <paramref name="path"/>: seçili gelecek yedek.</summary>
        public void OpenRestoreWizard(string path)
        {
            if (_wizard != null)
            {
                _wizard.Activate();
                return;
            }
            _wizard = new RestoreWizard(path, RestartAsAdmin) { Owner = IsVisible ? this : null };
            _wizard.Closed += (s, e) => { _wizard = null; _programsLoaded = false; };
            _wizard.Show();
        }

        /// <summary>
        /// Yeni kurulmuş bir Windows'ta (3 günden yeni) açıldıysa ve takılı bir diskte yedek varsa asistanı önerir.
        /// Her Windows kurulumu için bir kez sorar.
        /// </summary>
        async Task OfferRestoreOnNewWindowsAsync()
        {
            try
            {
                var (installed, _) = AppInfo.InstallDates();
                if (installed == null || DateTime.Now - installed.Value > TimeSpan.FromDays(3)) return;
                var stamp = installed.Value.ToString("s");
                if (Settings.FreshOffered == stamp) return;
                var found = await System.Threading.Tasks.Task.Run(() => RestoreRunner.FindBackups());
                if (found.Count == 0) return;
                Settings.FreshOffered = stamp;
                SaveSettings();
                var name = Path.GetFileNameWithoutExtension(found[0]);
                if (Confirm(T("Yeni kurulmuş bir Windows'a hoş geldin! Bir Kahya yedeği bulundu:\n{0}\n\nFormat sonrası asistanı açılsın mı? Programlarını, dosyalarını ve ayarlarını adım adım geri yükler.", name)))
                    OpenRestoreWizard(found[0]);
            }
            catch { }
        }

        /// <summary>Başka bir kopyadan (çift tıklanan .kahya) gelen istek varsa asistanı açar.</summary>
        public void CheckPendingRestore()
        {
            string path = null;
            try
            {
                if (!File.Exists(PendingRestoreFile)) return;
                path = File.ReadAllText(PendingRestoreFile).Trim();
                File.Delete(PendingRestoreFile);
            }
            catch { }
            if (!string.IsNullOrEmpty(path)) OpenRestoreWizard(path);
        }

        /// <summary>
        /// .kahya yedek paketlerini (ve eski sürümlerin klasör yedeklerindeki .kahya dosyalarını) bu Kahya'ya bağlar (sadece bu kullanıcı için, HKCU):
        /// çift tıklayınca asistan açılır. Exe taşındıysa yol güncellenir.
        /// </summary>
        static void RegisterKahyaFiles()
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                var command = $"\"{exe}\" --restore \"%1\"";
                // Uygulamanınkinden farklı simge (belge + Kahya rozeti): Kahya.exe ile yedek dosyası yan yana karışmasın.
                var iconFile = Path.Combine(Settings.Folder, "kahya-file.ico");
                var iconValue = File.Exists(iconFile) || ExtractFileIcon(iconFile) ? $"\"{iconFile}\",0" : $"\"{exe}\",0";
                using (var type = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Kahya.Backup"))
                    if (type?.OpenSubKey(@"shell\open\command")?.GetValue("") as string == command &&
                        type.OpenSubKey("DefaultIcon")?.GetValue("") as string == iconValue)
                        return;

                using (var ext = Registry.CurrentUser.CreateSubKey(@"Software\Classes" + "\\" + BackupManifest.Extension))
                    ext.SetValue("", "Kahya.Backup");
                using (var type = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Kahya.Backup"))
                {
                    type.SetValue("", T("Kahya format yedeği"));
                    using (var icon = type.CreateSubKey("DefaultIcon")) icon.SetValue("", iconValue);
                    using (var cmd = type.CreateSubKey(@"shell\open\command")) cmd.SetValue("", command);
                }
                SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        /// <summary>Exe'ye gömülü .kahya simgesini Data klasörüne yazar (tek dosya program: simge ayrı dağıtılmaz).</summary>
        static bool ExtractFileIcon(string path)
        {
            try
            {
                using (var res = typeof(MainWindow).Assembly.GetManifestResourceStream("kahya-file.ico"))
                {
                    if (res == null) return false;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var file = File.Create(path)) res.CopyTo(file);
                }
                return true;
            }
            catch { return false; }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
