using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    public partial class App : Application
    {
        /// <summary>Yönetici olarak yeniden başlatılınca açılacak sayfa (--tab N); yoksa -1.</summary>
        public static int StartTab { get; private set; } = -1;

        /// <summary>Yeniden başlatma tek tıkla bakım için istendiyse bakım penceresi kendiliğinden açılır (--maintenance).</summary>
        public static bool StartMaintenance { get; private set; }

        /// <summary>Açılınca format sonrası asistanı açılsın mı (--wizard [yedek] ya da eski .kahya için --restore yedek).</summary>
        public static bool StartWizard { get; private set; }

        /// <summary>Asistanda seçili gelecek yedek; yoksa null (asistan kendisi arar).</summary>
        public static string StartRestore { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            if (!NetFrameworkOk())
            {
                Shutdown();
                return;
            }
            base.OnStartup(e);
            UpdateChecker.CleanupOld();

            for (int i = 0; i < e.Args.Length; i++)
            {
                if (e.Args[i] == "--tab" && i + 1 < e.Args.Length && int.TryParse(e.Args[i + 1], out var tab)) StartTab = tab;
                if (e.Args[i] == "--maintenance") StartMaintenance = true;
                if (e.Args[i] == "--wizard" || e.Args[i] == "--restore")
                {
                    StartWizard = true;
                    if (i + 1 < e.Args.Length && !e.Args[i + 1].StartsWith("--")) StartRestore = e.Args[i + 1];
                }
            }

            Settings.Read();
            Lang.Init(Settings.Language);

            DispatcherUnhandledException += (s, ex) =>
            {
                ex.Handled = true;
                ReportError(ex.Exception);
            };
            TaskScheduler.UnobservedTaskException += (s, ex) =>
            {
                ex.SetObserved();
                LogError(ex.Exception);
            };

            if (e.Args.Any(a => a.Equals("/auto", StringComparison.OrdinalIgnoreCase) ||
                                a.Equals("--auto", StringComparison.OrdinalIgnoreCase)))
            {
                RunAuto();
                Shutdown();
                return;
            }

            // Tek kopya: zaten açıksa (ör. bildirim alanında) onu öne getir ve çık.
            // Yeniden başlatmalarda (--restart) eski kopyanın kapanmasını bekle.
            if (!AcquireSingleInstance(e.Args.Contains("--restart")))
            {
                Shutdown();
                return;
            }

            Theme.Init(Settings.Theme);
            if (Settings.TrayEnabled)
                Tray.Start();
            if (e.Args.Contains("--tray") && Tray.Active)
                return; // oturum açılışında: pencere açmadan bildirim alanında bekle

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        /// <summary>
        /// .NET Framework 4.8 yüklü mü (Release ≥ 528040)? Daha eski bir 4.x varsa program açılır ama bazı yerlerde çöker;
        /// bu yüzden en başta bakılır ve indirme sayfası önerilir. Bu metot 4.8'e özgü hiçbir şey kullanmamalı
        /// (ValueTuple, Lang, Settings…): eski sürümde de çalışabilmesi gerekir.
        /// </summary>
        static bool NetFrameworkOk()
        {
            int release = 0;
            try
            {
                using (var k = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)
                           .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                    release = k?.GetValue("Release") is int r ? r : 0;
            }
            catch { return true; } // okunamıyorsa engelleme
            if (release >= 528040) return true;

            bool tr = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr";
            var answer = MessageBox.Show(tr
                    ? "Kahya'nın çalışması için .NET Framework 4.8 gerekiyor; bu bilgisayardaki sürüm daha eski.\n\nMicrosoft'un indirme sayfası açılsın mı? Kurduktan sonra bilgisayarı yeniden başlatıp Kahya'yı tekrar aç."
                    : "Kahya needs .NET Framework 4.8; this PC has an older version.\n\nOpen Microsoft's download page? After installing, restart the PC and open Kahya again.",
                "Kahya", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Yes)
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://dotnet.microsoft.com/download/dotnet-framework/net48") { UseShellExecute = true }); } catch { }
            return false;
        }

        /// <summary>Beklenmeyen hataların ayrıntıları (yığın izi dahil) buraya yazılır.</summary>
        public static string ErrorLogPath => IssueReport.ErrorLogPath;

        static readonly System.Collections.Generic.HashSet<string> _shownErrors = new System.Collections.Generic.HashSet<string>();
        static bool _errorDialogOpen;

        static void LogError(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.AppendAllText(ErrorLogPath, $"──── {DateTime.Now:dd.MM.yyyy HH:mm:ss} · v{AppInfo.Version}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}", Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>
        /// Hatayı dosyaya yazar ve gösterir. Aynı hata (ör. her çizimde tekrarlanan) bir kez gösterilir,
        /// pencere açıkken gelen yeni hatalar sadece dosyaya yazılır: üst üste pencere yağmuru olmasın.
        /// </summary>
        static void ReportError(Exception ex)
        {
            LogError(ex);
            var key = ex.GetType().FullName + ex.Message + ex.StackTrace;
            if (_errorDialogOpen || !_shownErrors.Add(key)) return;
            _errorDialogOpen = true;
            try
            {
                var answer = MessageBox.Show(
                    T("{0}\n\nAyrıntılar şu dosyaya kaydedildi:\n{1}", ex.Message, ErrorLogPath) + "\n\n" +
                    T("Bu hatayı geliştiriciye bildirmek ister misin? GitHub'da hazır doldurulmuş bir sayfa açılır; ayrıntılar panoya da kopyalanır, istersen WhatsApp vb. ile de gönderebilirsin."),
                    T("Kahya - beklenmeyen hata"), MessageBoxButton.YesNo, MessageBoxImage.Error);
                if (answer == MessageBoxResult.Yes)
                {
                    try { Clipboard.SetText(IssueReport.Body(ex)); } catch { }
                    IssueReport.OpenOnGitHub(ex);
                }
            }
            finally { _errorDialogOpen = false; }
        }

        static Mutex _instance;
        static EventWaitHandle _showSignal;

        static bool AcquireSingleInstance(bool restarting)
        {
            _instance = new Mutex(true, @"Local\Kahya.SingleInstance", out bool created);
            if (!created)
            {
                bool got = false;
                if (restarting)
                {
                    try { got = _instance.WaitOne(10000); }
                    catch (AbandonedMutexException) { got = true; }
                }
                if (!got)
                {
                    try
                    {
                        // Çift tıklanan .kahya dosyasını açık olan kopyaya ilet (ShowMain okur).
                        if (StartRestore != null)
                            try { Directory.CreateDirectory(Settings.Folder); File.WriteAllText(Kahya.MainWindow.PendingRestoreFile, StartRestore); } catch { }
                        EventWaitHandle.OpenExisting(@"Local\Kahya.Show").Set();
                        return false;
                    }
                    catch
                    {
                        // Diğer kopyaya ulaşılamadı (ör. o yönetici olarak açık): ikinci bir kopya olarak devam et.
                        return true;
                    }
                }
            }

            try
            {
                _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Kahya.Show");
                var app = Current;
                new Thread(() =>
                {
                    while (_showSignal.WaitOne())
                        app.Dispatcher.BeginInvoke(new Action(Tray.ShowMain));
                }) { IsBackground = true }.Start();
            }
            catch { }
            return true;
        }

        /// <summary>
        /// Arayüzsüz otomatik bakım: kayıtlı seçimleri temizler, ayarlıysa uygulamaları ve eski sürücüleri günceller,
        /// sonucu last-run.log'a ve geçmişe yazar, bitince bildirim gösterir.
        /// </summary>
        static void RunAuto()
        {
            var log = new StringBuilder();
            Action<string> write = line => log.AppendLine(line);
            write(T("Kahya otomatik bakım — {0:dd.MM.yyyy HH:mm}", DateTime.Now));

            MaintenanceResult result = null;
            try
            {
                var items = Catalog.Build().Where(i => i.IsAvailable()).ToList();
                Settings.Load(items);
                var engine = new CleanEngine(write) { SkipRecentFiles = Settings.SkipRecentFiles };
                var ctx = new CleanContext(write, CancellationToken.None);
                result = Maintenance.Run(MaintenancePlan.FromSettings(items), engine, ctx);
                write(T("Sonuç: {0}", result.Summary()));
                History.Add(new HistoryEntry
                {
                    Date = DateTime.Now, Kind = "auto", Freed = result.Freed + result.DriverBytes,
                    Updated = result.Updated, Drivers = result.DriversRemoved,
                });
            }
            catch (Exception ex)
            {
                write(T("Hata: {0}", ex));
            }

            // Yeni ekran kartı sürücüsü varsa bildirime ekle (sürücüyü kendisi kurmaz).
            string gpuNote = null;
            try
            {
                var gpu = GpuDrivers.Check().FirstOrDefault(g => g.UpdateAvailable);
                if (gpu != null)
                {
                    gpuNote = T("Yeni ekran kartı sürücüsü var: {0}", gpu.LatestVersion);
                    write(gpuNote + " — " + gpu.Url);
                }
            }
            catch { }

            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.WriteAllText(Path.Combine(Settings.Folder, "last-run.log"), log.ToString(), Encoding.UTF8);
            }
            catch { }

            // Otomatik bakım yönetici olarak çalışır: açılış sürelerini kaydetmek için iyi bir an.
            try { BootTimes.Record(); } catch { }

            var lowSpace = SystemDrive.LowSpaceWarning();
            if (lowSpace != null) write(lowSpace);

            var text = result != null ? result.Summary() : T("Bir hata oluştu, ayrıntılar last-run.log dosyasında.");
            if (gpuNote != null) text += Environment.NewLine + gpuNote;
            if (lowSpace != null) text += Environment.NewLine + "⚠ " + lowSpace;
            if (result != null && result.UpdateFailed > 0)
                text += Environment.NewLine + T("Güncellenemeyenler: {0}", string.Join(", ", result.Failures.Select(f => f.Split(':')[0])));
            Notify(T("Otomatik bakım tamamlandı"), text);
        }

        /// <summary>Sistem tepsisinde birkaç saniyelik bildirim (Windows 10/11'de bildirim olarak görünür).</summary>
        static void Notify(string title, string text)
        {
            try
            {
                using (var icon = new System.Windows.Forms.NotifyIcon())
                {
                    var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                    icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                    icon.Text = "Kahya";
                    icon.Visible = true;
                    icon.ShowBalloonTip(8000, title, text, System.Windows.Forms.ToolTipIcon.Info);
                    var until = DateTime.Now.AddSeconds(8);
                    while (DateTime.Now < until)
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(100);
                    }
                    icon.Visible = false;
                }
            }
            catch { }
        }
    }
}
