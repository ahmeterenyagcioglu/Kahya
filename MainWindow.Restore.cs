using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Kahya.Core;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Format sonrası tek tıkla geri yükleme: yedeğin kökündeki <bilgisayar>.kahya dosyasından her şeyi sırayla geri yükler.
    public partial class MainWindow
    {
        /// <summary>Kahya zaten açıkken çift tıklanan .kahya dosyası buraya yazılır; açık olan kopya okuyup başlatır.</summary>
        public static string PendingRestoreFile => Path.Combine(Settings.Folder, "pending-restore.txt");

        void RestoreFromBackup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = T("Yedeğin içindeki .kahya dosyasını seç"),
                Filter = T("Kahya yedeği") + " (*.kahya)|*.kahya",
            };
            if (dialog.ShowDialog(this) != true) return;
            _ = RunRestoreAsync(dialog.FileName, confirmed: false);
        }

        /// <summary>Başka bir kopyadan (çift tıklanan .kahya) gelen geri yükleme isteği varsa başlatır.</summary>
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
            if (string.IsNullOrEmpty(path)) return;
            if (_busy)
            {
                Info(T("Kahya şu an başka bir işlem yapıyor. O bitince yedek dosyasını tekrar aç."));
                return;
            }
            _ = RunRestoreAsync(path, confirmed: false);
        }

        /// <summary>
        /// Sıra: sürücüler (internet için önce) → Wi-Fi → fontlar → kişisel klasörler → programlar → program ayarları → oyun kayıtları.
        /// Ayarlar ve oyunlar programlardan sonra gelir ki kurulum onların üzerine yazmasın. Her adım ötekilerden bağımsızdır;
        /// biri başarısız olursa diğerleri yine yapılır.
        /// </summary>
        async Task RunRestoreAsync(string path, bool confirmed)
        {
            BackupManifest m;
            try { m = BackupManifest.Load(path); }
            catch (Exception ex)
            {
                Info(T("Yedek dosyası okunamadı: {0}", ex.Message));
                return;
            }
            Tabs.SelectedItem = FormatTab;

            List<ProgramEntry> programs = null;
            if (m.Programs != null)
                try { programs = Core.ProgramList.Load(m.Full(m.Programs)).Where(p => p.Installable).ToList(); } catch { }
            var folders = m.Folders.Where(f => Directory.Exists(m.Full(f.Value)) && FormatBackup.KnownFolderPath(f.Key) != null).ToList();
            bool drivers = m.Drivers != null && (Directory.Exists(m.Full(m.Drivers)) || File.Exists(m.Full(m.Drivers)));
            bool Has(string rel) => rel != null && Directory.Exists(m.Full(rel));

            if (!confirmed)
            {
                var steps = new List<string>();
                if (drivers) steps.Add(T("Sürücüler") + (Elevation.IsAdmin ? "" : " " + T("(yönetici izni istenir)")));
                if (Has(m.Wifi)) steps.Add(T("Wi-Fi ağları"));
                if (Has(m.Fonts)) steps.Add(T("Fontlar"));
                if (folders.Count > 0) steps.Add(T("Kişisel klasörler ({0})", string.Join(", ", folders.Select(f => T(FolderTitle(f.Key))))));
                if (programs?.Count > 0) steps.Add(T("Programlar ({0} tane, winget ile en yeni sürümü)", programs.Count));
                if (Has(m.Settings)) steps.Add(T("Program ayarları"));
                if (Has(m.Games)) steps.Add(T("Oyun kayıtları ve Minecraft"));
                if (steps.Count == 0)
                {
                    Info(T("Bu yedekte geri yüklenecek bir şey bulunamadı. Dosyanın yedek klasörünün içinde durduğundan emin ol."));
                    return;
                }
                if (!Confirm(T("Yedek: {0} · {1:dd.MM.yyyy HH:mm}\n\nSırayla geri yüklenecek:\n{2}\n\nVar olan dosyaların daha yenisi varsa üzerine yazılmaz. Birkaç dakikadan birkaç saate kadar sürebilir (programların sayısına ve internet hızına göre).\n\nBaşlansın mı?",
                        m.Computer ?? "?", m.Date, string.Join("\n", steps.Select(s => "• " + s)))))
                    return;

                if (drivers && !Elevation.IsAdmin)
                {
                    if (Confirm(T("Sürücüleri kurmak için yönetici izni gerekiyor. Kahya yönetici olarak yeniden başlayıp geri yüklemeye kendiliğinden devam etsin mi?\n\n'Hayır' dersen sürücüler atlanır, diğerleri şimdi yapılır.")))
                    {
                        RestartAsAdmin($"--restore \"{path}\" --restore-confirmed");
                        return;
                    }
                    drivers = false;
                }
            }
            if (!Elevation.IsAdmin) drivers = false;

            // Kurulum araçları meşgul durumundan önce hazırlanır (kendi sorularını ve meşgul durumlarını açarlar).
            if (drivers && m.Drivers.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) && !await EnsureSevenZipAsync())
            {
                Log("! " + T("7-Zip olmadan .7z sürücü yedeği açılamaz; sürücüler atlandı."));
                drivers = false;
            }
            if (programs?.Count > 0 && !await EnsureWingetAsync())
            {
                Log("! " + T("winget olmadan programlar kurulamaz; programlar atlandı. Listeden sonra elle kurabilirsin: {0}", m.Full(m.Programs)));
                programs = null;
            }

            var token = SetBusy(T("geri yükleniyor…"));
            ShowLog(true);
            Log("══════════\n" + T("Yedekten geri yükleme: {0} · {1:dd.MM.yyyy HH:mm}", m.Computer ?? "?", m.Date));
            var ctx = Context(token);
            int ok = 0, failed = 0;
            bool reboot = false;

            async Task Step(string title, Func<bool> work)
            {
                token.ThrowIfCancellationRequested();
                TotalCaption.Text = T("geri yükleniyor: {0}", title.ToLowerInvariant());
                Log("▶ " + title);
                try
                {
                    if (await Task.Run(work)) { ok++; Log("   ✔ " + title); }
                    else { failed++; Log("   ✖ " + T("{0}: bazı öğeler geri yüklenemedi, ayrıntılar yukarıda.", title)); }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    Log("   ✖ " + title + ": " + ex.Message);
                }
            }

            try
            {
                if (drivers)
                    await Step(T("Sürücüler"), () =>
                    {
                        var folder = FormatBackup.Extract(m.Full(m.Drivers), ctx);
                        int code = FormatBackup.RestoreDrivers(folder, ctx);
                        if (code == 3010) reboot = true;
                        return code == 0 || code == 3010;
                    });
                if (Has(m.Wifi))
                    await Step(T("Wi-Fi ağları"), () => FormatBackup.RestoreWifi(m.Full(m.Wifi), ctx) > 0);
                if (Has(m.Fonts))
                    await Step(T("Fontlar"), () => { var (installed, skipped) = FormatExtras.RestoreFonts(m.Full(m.Fonts), ctx); ctx.Log("   " + T("{0} font kuruldu, {1} tanesi zaten vardı.", installed, skipped)); return true; });
                foreach (var f in folders)
                {
                    var dest = FormatBackup.KnownFolderPath(f.Key);
                    await Step(T(FolderTitle(f.Key)), () => FormatBackup.RestoreFolder(m.Full(f.Value), dest, ctx));
                }
                if (programs?.Count > 0)
                    await Step(T("Programlar"), () => InstallPrograms(programs, ctx));
                if (Has(m.Settings))
                    await Step(T("Program ayarları"), () => FormatExtras.RestoreAppSettings(m.Full(m.Settings), ctx) > 0);
                if (Has(m.Games))
                    await Step(T("Oyun kayıtları"), () => FormatExtras.RestoreAppSettings(m.Full(m.Games), ctx) > 0);
            }
            catch (OperationCanceledException)
            {
                Log(T("Geri yükleme iptal edildi."));
                return;
            }
            finally
            {
                ApplyBusy(false);
                _programsLoaded = false;
            }

            Log(failed > 0 ? T("Geri yükleme bitti: {0} adım tamam, {1} adımda sorun var.", ok, failed) : T("Geri yükleme bitti: {0} adım tamam ✔", ok));
            var end = failed > 0 ? T("Geri yükleme bitti. {0} adım tamam, {1} adımda sorun var (ayrıntılar işlem kaydında).", ok, failed) : T("Geri yükleme bitti, hepsi tamam ✔");
            if (reboot) end += "\n\n" + T("Sürücülerin bitmesi için bilgisayarı yeniden başlat.");
            if (m.Bookmarks != null && Directory.Exists(m.Full(m.Bookmarks)))
            {
                end += "\n\n" + T("Yer imlerini tarayıcı kendisi içe aktarır: Ayarlar › Yer imlerini içe aktar › HTML dosyası. 'yer-imleri' klasörü açılsın mı?");
                if (Confirm(end))
                    try { Process.Start(new ProcessStartInfo(m.Full(m.Bookmarks)) { UseShellExecute = true }); } catch { }
            }
            else Info(end);
        }

        /// <summary>Listedeki programlardan kurulu olmayanları sırayla kurar. Hepsi kurulduysa true.</summary>
        static bool InstallPrograms(List<ProgramEntry> programs, CleanContext ctx)
        {
            HashSet<string> installed;
            try { installed = Winget.InstalledIds(ctx); }
            catch (OperationCanceledException) { throw; }
            catch { installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }

            var todo = programs.Where(p => !installed.Contains(p.Id)).ToList();
            ctx.Log("   " + T("{0} program kurulacak ({1} tanesi zaten kurulu).", todo.Count, programs.Count - todo.Count));
            int failed = 0;
            for (int i = 0; i < todo.Count; i++)
            {
                var p = todo[i];
                ctx.Progress?.Invoke(T("{0} kuruluyor ({1}/{2})…", p.Name, i + 1, todo.Count));
                ctx.Log("   ▶ " + p.Name);
                var app = new CatalogApp { Id = p.Id, Name = p.Name, Source = string.IsNullOrEmpty(p.Source) ? "winget" : p.Source };
                if (!Winget.Install(app, ctx))
                {
                    failed++;
                    ctx.Log("     ✖ " + T("{0} kurulamadı: {1}", p.Name, app.Error));
                }
            }
            return failed == 0;
        }

        static string FolderTitle(string key)
        {
            switch (key)
            {
                case "Desktop": return "Masaüstü";
                case "Documents": return "Belgeler";
                case "Downloads": return "İndirilenler";
                case "Pictures": return "Resimler";
                case "Videos": return "Videolar";
                case "Music": return "Müzik";
                default: return key;
            }
        }

        /// <summary>
        /// .kahya dosyalarını bu Kahya'ya bağlar (sadece bu kullanıcı için, HKCU): çift tıklayınca geri yükleme açılır.
        /// Exe taşındıysa yol güncellenir.
        /// </summary>
        static void RegisterKahyaFiles()
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                var command = $"\"{exe}\" --restore \"%1\"";
                using (var cmd = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Kahya.Backup\shell\open\command"))
                    if (cmd?.GetValue("") as string == command) return;

                using (var ext = Registry.CurrentUser.CreateSubKey(@"Software\Classes" + "\\" + BackupManifest.Extension))
                    ext.SetValue("", "Kahya.Backup");
                using (var type = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Kahya.Backup"))
                {
                    type.SetValue("", T("Kahya format yedeği"));
                    using (var icon = type.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exe}\",0");
                    using (var cmd = type.CreateSubKey(@"shell\open\command")) cmd.SetValue("", command);
                }
                SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
