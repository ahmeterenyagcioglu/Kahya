using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Başlangıç sekmesi: açılışta başlayan uygulamalar, zamanlanmış görevler, servisler
    public partial class MainWindow
    {
        readonly ObservableCollection<StartupEntry> _startup = new ObservableCollection<StartupEntry>();
        readonly ObservableCollection<BackgroundItem> _tasks = new ObservableCollection<BackgroundItem>();
        readonly ObservableCollection<BackgroundItem> _services = new ObservableCollection<BackgroundItem>();
        bool _startupLoaded, _tasksLoaded, _servicesLoaded;
        bool _startupReverting;
        string _startupMode = "apps";

        void InitStartup()
        {
            StartupList.ItemsSource = _startup;
            TaskList.ItemsSource = _tasks;
            ServiceList.ItemsSource = _services;
            UpdateStartupMode();
        }

        void LoadStartup()
        {
            foreach (var s in _startup)
                s.PropertyChanged -= Startup_PropertyChanged;
            _startup.Clear();

            try
            {
                foreach (var s in StartupManager.Load())
                {
                    s.PropertyChanged += Startup_PropertyChanged;
                    _startup.Add(s);
                }
            }
            catch (Exception ex)
            {
                Log("! " + T("Başlangıç listesi okunamadı: {0}", ex.Message));
            }

            _startupLoaded = true;
            UpdateStartupInfo();
        }

        void Startup_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(StartupEntry.IsEnabled) || _startupReverting)
                return;

            var entry = (StartupEntry)sender;
            try
            {
                StartupManager.SetEnabled(entry, entry.IsEnabled);
                Log(entry.IsEnabled ? "✔ " + T("Açılışta başlayacak: {0}", entry.Name) : "⏸ " + T("Açılışta artık başlamayacak: {0}", entry.Name));
            }
            catch (Exception ex)
            {
                _startupReverting = true;
                entry.IsEnabled = !entry.IsEnabled;
                _startupReverting = false;
                Log("! " + T("{0} değiştirilemedi: {1}", entry.Name, ex.Message));
            }
            UpdateStartupInfo();
            UpdateHeader();
        }

        // ───────────── görünüm seçimi ─────────────

        async void StartupMode_Click(object sender, RoutedEventArgs e)
        {
            _startupMode = (sender as FrameworkElement)?.Tag as string ?? "apps";
            UpdateStartupMode();
            if (_startupMode == "tasks" && !_tasksLoaded) await LoadTasksAsync();
            else if (_startupMode == "services" && !_servicesLoaded) await LoadServicesAsync();
            UpdateStartupInfo();
            UpdateHeader();
        }

        void UpdateStartupMode()
        {
            Highlight(StartupAppsBtn, _startupMode == "apps");
            Highlight(StartupTasksBtn, _startupMode == "tasks");
            Highlight(StartupServicesBtn, _startupMode == "services");
            StartupAppsPanel.Visibility = _startupMode == "apps" ? Visibility.Visible : Visibility.Collapsed;
            StartupTasksPanel.Visibility = _startupMode == "tasks" ? Visibility.Visible : Visibility.Collapsed;
            StartupServicesPanel.Visibility = _startupMode == "services" ? Visibility.Visible : Visibility.Collapsed;
            UpdateStartupInfo();
        }

        async void StartupRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_startupMode == "tasks") await LoadTasksAsync();
            else if (_startupMode == "services") await LoadServicesAsync();
            else LoadStartup();
            UpdateStartupInfo();
            UpdateHeader();
        }

        (string count, string caption) StartupHeader()
        {
            switch (_startupMode)
            {
                case "tasks": return (_tasksLoaded ? _tasks.Count(t => t.IsEnabled).ToString() : "—", T("görev açık"));
                case "services": return (_servicesLoaded ? _services.Count(t => t.IsEnabled).ToString() : "—", T("servis açılışta başlıyor"));
                default: return (_startup.Count(s => s.IsEnabled).ToString(), T("uygulama açılışta başlıyor"));
            }
        }

        void UpdateStartupInfo()
        {
            if (StartupInfo == null) return;
            StartupEmpty.Visibility = Visibility.Collapsed;
            switch (_startupMode)
            {
                case "tasks":
                {
                    int on = _tasks.Count(t => t.IsEnabled);
                    StartupInfo.Text = _tasksLoaded
                        ? T("{0} açık · {1} kapalı · kapattığın görev çalışmaz ama silinmez", on, _tasks.Count - on)
                        : T("Görevler okunuyor…");
                    ShowStartupEmpty(_tasksLoaded && _tasks.Count == 0, T("Microsoft dışında zamanlanmış görev yok ✔"));
                    break;
                }
                case "services":
                {
                    int on = _services.Count(t => t.IsEnabled);
                    StartupInfo.Text = _servicesLoaded
                        ? T("{0} açılışta başlıyor · {1} elle · kapatılan servis açılışta başlamaz, gerekirse programı onu yine çalıştırabilir", on, _services.Count - on)
                        : T("Servisler okunuyor…");
                    ShowStartupEmpty(_servicesLoaded && _services.Count == 0, T("Microsoft dışında servis yok ✔"));
                    break;
                }
                default:
                {
                    int on = _startup.Count(s => s.IsEnabled);
                    StartupInfo.Text = T("{0} açık · {1} kapalı · değişiklik anında kaydedilir, sonraki açılışta geçerli olur", on, _startup.Count - on);
                    int high = _startup.Count(s => s.IsEnabled && s.Impact == "high");
                    if (high > 0) StartupInfo.Text += " · " + T("{0} tanesi açılışı belirgin yavaşlatıyor", high);
                    else if (!Elevation.IsAdmin) StartupInfo.Text += " · " + T("açılışa etkisini görmek için yönetici olarak aç");
                    break;
                }
            }
        }

        void ShowStartupEmpty(bool show, string text)
        {
            StartupEmpty.Text = text;
            StartupEmpty.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        // ───────────── zamanlanmış görevler ve servisler ─────────────

        async Task LoadTasksAsync()
        {
            var token = SetBusy(T("okunuyor…"));
            try
            {
                var list = await Task.Run(() => BackgroundItems.LoadTasks(Context(token)));
                Fill(_tasks, list);
                _tasksLoaded = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log("! " + T("Görevler okunamadı: {0}", ex.Message));
            }
            finally
            {
                ApplyBusy(false);
                UpdateStartupInfo();
            }
        }

        async Task LoadServicesAsync()
        {
            SetBusy(T("okunuyor…"));
            try
            {
                var list = await Task.Run(() => BackgroundItems.LoadServices());
                Fill(_services, list);
                _servicesLoaded = true;
            }
            catch (Exception ex)
            {
                Log("! " + T("Servisler okunamadı: {0}", ex.Message));
            }
            finally
            {
                ApplyBusy(false);
                UpdateStartupInfo();
            }
        }

        void Fill(ObservableCollection<BackgroundItem> target, System.Collections.Generic.List<BackgroundItem> list)
        {
            foreach (var i in target) i.PropertyChanged -= Background_PropertyChanged;
            target.Clear();
            foreach (var i in list)
            {
                i.PropertyChanged += Background_PropertyChanged;
                target.Add(i);
            }
        }

        async void Background_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BackgroundItem.IsEnabled) || _startupReverting) return;
            var item = (BackgroundItem)sender;
            bool on = item.IsEnabled;
            string error;
            try
            {
                error = item.Kind == "task"
                    ? await Task.Run(() => BackgroundItems.SetTaskEnabled(item, on, new CleanContext(_ => { }, CancellationToken.None)))
                    : await Task.Run(() => BackgroundItems.SetServiceEnabled(item, on));
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (error == null)
            {
                Log(item.Kind == "task"
                    ? (on ? "✔ " + T("Görev açıldı: {0}", item.Name) : "⏸ " + T("Görev kapatıldı: {0}", item.Name))
                    : (on ? "✔ " + T("Servis açılışta başlayacak: {0}", item.Name) : "⏸ " + T("Servis artık açılışta başlamayacak (elle): {0}", item.Name)));
            }
            else
            {
                _startupReverting = true;
                item.IsEnabled = !on;
                _startupReverting = false;
                Log("! " + T("{0} değiştirilemedi: {1}", item.Name, error));
            }
            UpdateStartupInfo();
            UpdateHeader();
        }
    }
}
