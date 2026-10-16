using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Kahya.Core;
using Forms = System.Windows.Forms;
using static Kahya.Core.Lang;

namespace Kahya
{
    /// <summary>
    /// Bildirim alanı simgesi. Açıkken pencereyi kapatmak programı kapatmaz, gizler; arka planda birkaç saatte bir
    /// disk alanı, uygulama güncellemeleri, ekran kartı sürücüsü ve Windows Update'e bakıp gerekirse bildirim gösterir.
    /// </summary>
    static class Tray
    {
        static Forms.NotifyIcon _icon;
        static DispatcherTimer _timer;
        static string _lastNotice;
        static bool _hintShown;
        /// <summary>Son bildirime tıklanınca açılacak sekme (yoksa sadece pencere açılır).</summary>
        static Action<MainWindow> _onClick;

        public static bool Active => _icon != null;

        /// <summary>Gerçekten çıkılıyor (pencere kapatılınca gizlenmesin).</summary>
        public static bool Exiting { get; private set; }

        public static void Start()
        {
            if (_icon != null) return;
            _icon = new Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName),
                Text = "Kahya",
                Visible = true,
            };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(T("Kahya'yı aç"), null, (s, e) => ShowMain());
            menu.Items.Add(T("Şimdi kontrol et"), null, async (s, e) => await CheckAsync(force: true));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(T("Çıkış"), null, (s, e) => Exit());
            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += (s, e) => ShowMain();
            _icon.BalloonTipClicked += (s, e) =>
            {
                ShowMain();
                // Önbellek uyarısı Temizlik'e (silmeden önce kullanıcı seçsin), disk uyarısı Format yardımcısına götürür.
                if (Application.Current.MainWindow is MainWindow w) _onClick?.Invoke(w);
            };

            // İlk kontrol açılıştan birkaç dakika sonra (oturum açılışını yavaşlatmasın), sonra 6 saatte bir.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
            _timer.Tick += async (s, e) =>
            {
                _timer.Interval = TimeSpan.FromHours(6);
                await CheckAsync(force: false);
            };
            _timer.Start();
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        public static void Stop()
        {
            _timer?.Stop();
            _timer = null;
            if (_icon != null)
            {
                _icon.Visible = false;
                _icon.Dispose();
                _icon = null;
            }
            if (Application.Current != null && !Exiting)
                Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        public static void ShowMain()
        {
            var w = Application.Current.MainWindow as MainWindow;
            if (w == null)
            {
                w = new MainWindow();
                Application.Current.MainWindow = w;
            }
            w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            w.CheckPendingRestore();
        }

        /// <summary>Programı tamamen kapatır (bildirim alanından da).</summary>
        public static void Exit()
        {
            Exiting = true;
            Stop();
            Application.Current.Shutdown();
        }

        /// <summary>Pencere kapatılınca bir kez "arka planda çalışıyorum" hatırlatması.</summary>
        public static void HiddenHint()
        {
            if (_hintShown) return;
            _hintShown = true;
            _onClick = null;
            Notify("Kahya", T("Kahya bildirim alanında çalışmaya devam ediyor. Tamamen kapatmak için simgeye sağ tıkla › Çıkış."));
        }

        public static void Notify(string title, string text, bool warning = false) =>
            _icon?.ShowBalloonTip(10000, title, text, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);

        /// <summary>
        /// Uygulama, tarayıcı ve video/fotoğraf programı önbelleklerinden eşiği (ayarlar › önbellek uyarısı) geçenler.
        /// Sadece ölçer, hiçbir şey silmez.
        /// </summary>
        static List<CleanItem> BloatedCaches()
        {
            long limit = Settings.CacheAlertGb * 1024L * 1024 * 1024;
            if (limit <= 0) return new List<CleanItem>();
            var groups = new[] { Catalog.GApps, Catalog.GCreative, Catalog.GBrowsers };
            var items = Catalog.Build()
                .Where(i => groups.Contains(i.GroupKey) && !i.Sensitive && !i.Locked && i.Rules.Count > 0 && i.IsAvailable())
                .ToList();
            new CleanEngine(_ => { }).Analyze(items, CancellationToken.None);
            return items.Where(i => i.Size >= limit).OrderByDescending(i => i.Size).ToList();
        }

        static async Task CheckAsync(bool force)
        {
            var notes = new List<string>();
            bool cacheAlert = false;
            List<PhysicalDiskInfo> riskyDisks = null;
            try
            {
                await Task.Run(() =>
                {
                    // Disk bozuluyorsa her şeyden önce o söylenir: veriler kaybolmadan yedek alınmalı.
                    try
                    {
                        riskyDisks = SystemCare.DiskHealth(new CleanContext(_ => { }, CancellationToken.None)).Where(d => d.AtRisk).ToList();
                        foreach (var d in riskyDisks)
                            notes.Add("⚠ " + T("{0}: {1} · verilerini hemen yedekle (tıkla: Format yardımcısı)", d.Name, d.RiskText));
                    }
                    catch { }

                    var low = SystemDrive.LowSpaceWarning();
                    if (low != null) notes.Add("⚠ " + low);

                    try
                    {
                        if (Winget.IsInstalled())
                        {
                            int n = Winget.ListUpgrades(new CleanContext(_ => { }, CancellationToken.None))
                                          .Count(p => !Settings.IgnoredUpdates.Contains(p.Id));
                            if (n > 0) notes.Add(T("{0} uygulamanın yeni sürümü var", n));
                        }
                    }
                    catch { }

                    try
                    {
                        var gpu = GpuDrivers.Check().FirstOrDefault(g => g.UpdateAvailable && g.LatestVersion != Settings.GpuDismissed);
                        if (gpu != null) notes.Add(T("Yeni ekran kartı sürücüsü var: {0}", gpu.LatestVersion));
                    }
                    catch { }

                    if (WindowsUpdateInfo.RebootRequired())
                        notes.Add(T("Windows güncellemesini bitirmek için yeniden başlatma bekleniyor"));

                    try
                    {
                        var big = BloatedCaches();
                        if (big.Count > 0)
                        {
                            notes.AddRange(big.Select(i => T("{0} {1} oldu", i.Name, Format.Bytes(i.Size))));
                            cacheAlert = true;
                        }
                    }
                    catch { }
                });
            }
            catch { }
            bool diskAlert = riskyDisks?.Count > 0;
            _onClick = diskAlert ? (Action<MainWindow>)(w => w.ShowFormatTab())
                     : cacheAlert ? w => w.ShowCleanTab()
                     : (Action<MainWindow>)null;

            var text = string.Join(Environment.NewLine, notes);
            if (notes.Count == 0)
            {
                if (force) Notify("Kahya", T("Her şey yolunda ✔"));
                return;
            }
            if (!force && text == _lastNotice) return; // aynı şeyi tekrar tekrar söyleme
            _lastNotice = text;
            Notify(diskAlert ? T("Kahya - disk uyarısı") : "Kahya", text, diskAlert);
        }

        // ───────────── Windows açılınca başlat ─────────────

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "Kahya";

        public static bool Autostart
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                        return k?.GetValue(RunName) != null;
                }
                catch { return false; }
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(RunName, $"\"{Process.GetCurrentProcess().MainModule.FileName}\" --tray");
                    else k.DeleteValue(RunName, false);
                }
            }
        }
    }
}
