using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Sistem › Performans: şu an ne yavaşlatıyor (Core/LiveUsage.cs). Yalnızca sayfa açıkken 2 saniyede bir ölçer.
    public partial class MainWindow
    {
        readonly LiveUsage _live = new LiveUsage();
        DispatcherTimer _perfTimer;
        bool _perfSampling;

        /// <summary>Kapatılması Windows'u bozacak ya da Kahya'nın kendisi olan süreçler.</summary>
        static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "svchost", "csrss", "wininit", "winlogon", "services", "lsass", "smss", "dwm", "explorer", "fontdrvhost", "sihost",
            "ctfmon", "conhost", "RuntimeBroker", "StartMenuExperienceHost", "ShellExperienceHost", "SearchHost", "TextInputHost",
            "audiodg", "spoolsv", "MsMpEng", "NisSrv", "SecurityHealthService", "dllhost", "taskhostw", "WmiPrvSE", "Kahya",
        };

        void StartPerf()
        {
            if (_perfTimer == null)
            {
                _perfTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _perfTimer.Tick += async (s, e) => await SamplePerfAsync();
            }
            _perfTimer.Start();
            _ = SamplePerfAsync();
        }

        void StopPerf() => _perfTimer?.Stop();

        async Task SamplePerfAsync()
        {
            if (_perfSampling || !IsVisible || WindowState == WindowState.Minimized) return;
            _perfSampling = true;
            try
            {
                var snap = await Task.Run(() => _live.Sample());
                if (Tabs.SelectedItem != PerfTab) return;
                PerfCpu.Text = Format.Percent(snap.Cpu);
                PerfCpuBar.Value = snap.Cpu;
                double memPct = snap.MemoryTotal > 0 ? 100.0 * snap.MemoryUsed / snap.MemoryTotal : 0;
                PerfMem.Text = T("{0} / {1}", Format.Bytes(snap.MemoryUsed), Format.Bytes(snap.MemoryTotal));
                PerfMemBar.Value = memPct;
                PerfVerdict.Text = snap.Verdict ?? T("Şu an her şey yolunda; bilgisayarı yavaşlatan bir şey yok.");
                PerfVerdict.SetResourceReference(TextBlock.ForegroundProperty, snap.Level == "bad" ? "Bad" : snap.Level == "warn" ? "Warn" : "Good");
                PerfList.ItemsSource = snap.Apps.Take(40).ToList();
            }
            catch { }
            finally { _perfSampling = false; }
        }

        void PerfClose_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is LiveApp a)) return;
            if (Protected.Contains(a.Key))
            {
                Info(T("{0} Windows'un bir parçası; kapatılırsa sistem bozulabilir.", a.Name));
                return;
            }
            if (!Confirm(T("{0} kapatılsın mı? Kaydedilmemiş işin varsa kaybolabilir.", a.Name))) return;
            int n = LiveUsage.Close(a.Key);
            Log(n > 0 ? "⏹ " + T("{0} kapatıldı.", a.Name) : "! " + T("{0} kapatılamadı (yönetici gerekebilir).", a.Name));
            _ = SamplePerfAsync();
        }
    }
}
