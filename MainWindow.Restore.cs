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

            // Sıkıştırılmış yedekte içerik arşiv açılmadan görülemez; o zaman .kahya'da yazanlara güvenilir.
            var archive = m.Archive != null ? Path.Combine(m.Root, m.Archive) : null;
            bool archived = archive != null && File.Exists(archive);
            if (m.Archive != null && !archived)
            {
                Info(T("Yedeğin arşivi ({0}) .kahya dosyasının yanında bulunamadı. Yedek klasörünü olduğu gibi kopyaladığından emin ol.", m.Archive));
                return;
            }

            List<ProgramEntry> programs = null;
            void LoadPrograms()
            {
                programs = null;
                if (m.Programs != null)
                    try { programs = Core.ProgramList.Load(m.Full(m.Programs)).Where(p => p.Installable).ToList(); } catch { }
            }
            bool Has(string rel) => rel != null && (Directory.Exists(m.Full(rel)) || archived && m.Extracted == null);
            if (!archived) LoadPrograms();
            var folderKeys = m.Folders.Keys.Where(k => FormatBackup.KnownFolderPath(k) != null && Has(m.Folders[k])).ToList();
            bool drivers = m.Drivers != null && (Directory.Exists(m.Full(m.Drivers)) || File.Exists(m.Full(m.Drivers)));

            if (!confirmed)
            {
                var steps = new List<string>();
                if (drivers) steps.Add(T("Sürücüler") + (Elevation.IsAdmin ? "" : " " + T("(yönetici izni istenir)")));
                if (Has(m.Wifi)) steps.Add(T("Wi-Fi ağları"));
                if (Has(m.Fonts)) steps.Add(T("Fontlar"));
                if (folderKeys.Count > 0) steps.Add(T("Kişisel klasörler ({0})", string.Join(", ", folderKeys.Select(k => T(FolderTitle(k))))));
                if (programs?.Count > 0) steps.Add(T("Programlar ({0} tane, winget ile en yeni sürümü)", programs.Count));
                else if (archived && m.Programs != null) steps.Add(T("Programlar (winget ile en yeni sürümü)"));
                if (Has(m.Settings)) steps.Add(T("Program ayarları"));
                if (Has(m.Games)) steps.Add(T("Oyun kayıtları ve Minecraft"));
                if (archived) steps.Insert(drivers ? 1 : 0, T("Önce {0} açılır", m.Archive));
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

            // winget meşgul durumundan önce hazırlanır (kendi sorusunu ve meşgul durumunu açar).
            // Arşivler için 7-Zip gerekmez: yoksa Windows'un kendi tar'ı açar.
            bool wingetOk = true;
            if ((programs?.Count > 0 || archived && m.Programs != null) && !await EnsureWingetAsync())
            {
                Log("! " + T("winget olmadan programlar kurulamaz; programlar atlandı. Listeden sonra elle kurabilirsin: {0}", m.Programs));
                wingetOk = false;
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

            string extracted = null, bookmarksCopy = null;
            try
            {
                // Sürücüler ve Wi-Fi arşivin dışında: internet için önce onlar, sonra arşiv.
                if (drivers)
                    await Step(T("Sürücüler"), () =>
                    {
                        var folder = FormatBackup.Extract(m.Full(m.Drivers), ctx);
                        int code = FormatBackup.RestoreDrivers(folder, ctx);
                        if (code == 3010) reboot = true;
                        return code == 0 || code == 3010;
                    });
                if (m.Wifi != null && Directory.Exists(m.Full(m.Wifi)))
                    await Step(T("Wi-Fi ağları"), () => FormatBackup.RestoreWifi(m.Full(m.Wifi), ctx) > 0);

                if (archived)
                {
                    extracted = ExtractTarget(archive);
                    bool opened = false;
                    await Step(T("Arşiv açılıyor ({0})", Format.Bytes(new FileInfo(archive).Length)), () =>
                    {
                        FormatBackup.Extract(archive, ctx, extracted);
                        return opened = true;
                    });
                    if (!opened) throw new InvalidOperationException(T("Arşiv açılamadı; geri kalan adımlar yapılamıyor."));
                    m.Extracted = extracted;
                    LoadPrograms();
                    folderKeys = m.Folders.Keys.Where(k => FormatBackup.KnownFolderPath(k) != null && Directory.Exists(m.Full(m.Folders[k]))).ToList();
                }
                if (!wingetOk) programs = null;

                if (Has(m.Fonts))
                    await Step(T("Fontlar"), () => { var (installed, skipped) = FormatExtras.RestoreFonts(m.Full(m.Fonts), ctx); ctx.Log("   " + T("{0} font kuruldu, {1} tanesi zaten vardı.", installed, skipped)); return true; });
                foreach (var key in folderKeys)
                {
                    var dest = FormatBackup.KnownFolderPath(key);
                    var source = m.Full(m.Folders[key]);
                    await Step(T(FolderTitle(key)), () => FormatBackup.RestoreFolder(source, dest, ctx));
                }
                if (programs?.Count > 0)
                    await Step(T("Programlar"), () => InstallPrograms(programs, ctx));
                if (Has(m.Settings))
                    await Step(T("Program ayarları"), () => FormatExtras.RestoreAppSettings(m.Full(m.Settings), ctx) > 0);
                if (Has(m.Games))
                    await Step(T("Oyun kayıtları"), () => FormatExtras.RestoreAppSettings(m.Full(m.Games), ctx) > 0);

                // Yer imlerini tarayıcı kendisi içe aktarır; açılan arşiv silineceği için Masaüstü'ne kopyalanır.
                if (extracted != null && m.Bookmarks != null && Directory.Exists(m.Full(m.Bookmarks)))
                {
                    bookmarksCopy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), T("Kahya - yer imleri"));
                    var from = m.Full(m.Bookmarks);
                    await Task.Run(() => FormatBackup.RestoreFolder(from, bookmarksCopy, ctx));
                }
            }
            catch (OperationCanceledException)
            {
                Log(T("Geri yükleme iptal edildi."));
                return;
            }
            catch (Exception ex)
            {
                failed++;
                Log("! " + ex.Message);
            }
            finally
            {
                if (extracted != null)
                {
                    var dir = extracted;
                    await Task.Run(() => { try { Directory.Delete(dir, true); } catch { } });
                    m.Extracted = null;
                }
                ApplyBusy(false);
                _programsLoaded = false;
            }

            Log(failed > 0 ? T("Geri yükleme bitti: {0} adım tamam, {1} adımda sorun var.", ok, failed) : T("Geri yükleme bitti: {0} adım tamam ✔", ok));
            var end = failed > 0 ? T("Geri yükleme bitti. {0} adım tamam, {1} adımda sorun var (ayrıntılar işlem kaydında).", ok, failed) : T("Geri yükleme bitti, hepsi tamam ✔");
            if (reboot) end += "\n\n" + T("Sürücülerin bitmesi için bilgisayarı yeniden başlat.");
            var bookmarks = bookmarksCopy ?? (m.Bookmarks != null ? m.Full(m.Bookmarks) : null);
            if (bookmarks != null && Directory.Exists(bookmarks))
            {
                end += "\n\n" + T("Yer imlerini tarayıcı kendisi içe aktarır: Ayarlar › Yer imlerini içe aktar › HTML dosyası. 'yer-imleri' klasörü açılsın mı?");
                if (Confirm(end))
                    try { Process.Start(new ProcessStartInfo(bookmarks) { UseShellExecute = true }); } catch { }
            }
            else Info(end);
        }

        /// <summary>
        /// Arşivin açılacağı yer: format sonrası C: genelde boş olduğu için Temp; yer yetmezse arşivin yanı (USB / ikinci disk).
        /// </summary>
        static string ExtractTarget(string archive)
        {
            long need = (long)(new FileInfo(archive).Length * 1.6) + 1024L * 1024 * 1024;
            var temp = Path.Combine(Path.GetTempPath(), "Kahya-geri-yukleme");
            try
            {
                if (new DriveInfo(Path.GetPathRoot(temp)).AvailableFreeSpace > need) return temp;
            }
            catch { }
            return Path.Combine(Path.GetDirectoryName(archive), "_" + T("acilan"));
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
