using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    /// <summary>
    /// Tek tıkla bakımın neyi yapacağını ve otomatik bakımın ne zaman çalışacağını ayarlar.
    /// Seçimler Settings'e yazılır; otomatik bakım da aynılarını kullanır.
    /// </summary>
    public partial class MaintenanceDialog : Window
    {
        public sealed class Choice : INotifyPropertyChanged
        {
            public CleanItem Item { get; set; }
            public string Name => Item.Name;
            public string Group => Item.Group;
            public string Description => Item.Description;

            bool _isChecked;
            public bool IsChecked
            {
                get => _isChecked;
                set { if (_isChecked != value) { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); } }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        readonly List<Choice> _choices;
        string _mode = Settings.ScheduleMode;
        DayOfWeek _day = Settings.ScheduleDay;
        int _hour = Settings.ScheduleHour;

        /// <summary>Kaydet ya da Başlat'a basıldı (ayarlar Settings'e yazıldı).</summary>
        public bool Saved { get; private set; }
        public bool ShutdownAfter => ShutdownBox.IsChecked == true;

        /// <param name="scheduled">Otomatik bakım görevi şu an kayıtlı mı (değilse "Kapalı" seçili gelir).</param>
        public MaintenanceDialog(IList<CleanItem> items, bool scheduled)
        {
            InitializeComponent();
            Lang.TranslateTree(this);
            Title = T("Tek tıkla bakım");
            SourceInitialized += (s, e) => TitleBar.Apply(this);
            if (!scheduled) _mode = "off";

            _choices = items.Select(i => new Choice { Item = i, IsChecked = Settings.InMaintenance(i) }).ToList();
            var view = CollectionViewSource.GetDefaultView(_choices);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Choice.Group)));
            CleanList.ItemsSource = view;

            CleanBox.IsChecked = Settings.MaintenanceClean;
            UpdatesBox.IsChecked = Settings.MaintenanceUpdates;
            DriversBox.IsChecked = Settings.MaintenanceDrivers;
            OptimizeBox.IsChecked = Settings.MaintenanceOptimize;
            RestorePointBox.IsChecked = Settings.RestorePointBeforeUpdates;

            int ignored = Settings.IgnoredUpdates.Count;
            UpdatesInfo.Text = ignored > 0
                ? T("Atladığın {0} uygulama hariç · güncellenen program kapanabilir", ignored)
                : T("Hepsi · güncellenen program kapanabilir");
            UpdatesInfo.ToolTip = T("Bir uygulamayı atlamak için Güncellemeler sayfasında satırındaki 'Atla'ya bas.");

            RefreshClean();
            RefreshSchedule();
        }

        // ───────────── temizlik kategorileri ─────────────

        void Changed(object sender, RoutedEventArgs e) => RefreshClean();

        void RefreshClean()
        {
            int n = _choices.Count(c => c.IsChecked);
            CleanInfo.Text = T("{0} kategori seçili", n);
            CleanExpandBtn.IsEnabled = CleanBox.IsChecked == true;
            if (CleanBox.IsChecked != true) CleanPanel.Visibility = Visibility.Collapsed;
            CleanExpandBtn.Content = CleanPanel.Visibility == Visibility.Visible ? T("Kategoriler ▴") : T("Kategoriler ▾");
        }

        void CleanExpand_Click(object sender, RoutedEventArgs e)
        {
            CleanPanel.Visibility = CleanPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            RefreshClean();
        }

        void CleanRecommended_Click(object sender, RoutedEventArgs e) { _choices.ForEach(c => c.IsChecked = c.Item.Recommended); RefreshClean(); }
        void CleanFromTab_Click(object sender, RoutedEventArgs e) { _choices.ForEach(c => c.IsChecked = c.Item.IsChecked); RefreshClean(); }
        void CleanNone_Click(object sender, RoutedEventArgs e) { _choices.ForEach(c => c.IsChecked = false); RefreshClean(); }

        // ───────────── zamanlama ─────────────

        void Mode_Click(object sender, RoutedEventArgs e) { _mode = (string)((FrameworkElement)sender).Tag; RefreshSchedule(); }
        void Day_Click(object sender, RoutedEventArgs e) { _day = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), (string)((FrameworkElement)sender).Tag); RefreshSchedule(); }
        void Hour_Click(object sender, RoutedEventArgs e) { _hour = int.Parse((string)((FrameworkElement)sender).Tag); RefreshSchedule(); }

        void RefreshSchedule()
        {
            Mark(ModePanel, b => (string)b.Tag == _mode);
            Mark(DayPanel, b => (string)b.Tag == _day.ToString());
            Mark(HourPanel, b => (string)b.Tag == _hour.ToString());
            WhenPanel.Visibility = _mode == "off" ? Visibility.Collapsed : Visibility.Visible;
            DayBar.Visibility = _mode == "weekly" || _mode == "biweekly" ? Visibility.Visible : Visibility.Collapsed;
            ScheduleInfo.Text = _mode == "off"
                ? T("Sadece sen başlatınca çalışır.")
                : T("{0}. Bilgisayar kapalıysa ilk açılışta yapılır, bitince bildirim gösterilir.", Scheduler.Describe(_mode, _day, _hour));
        }

        void Mark(Panel panel, Func<Button, bool> active)
        {
            foreach (var b in panel.Children.OfType<Button>())
            {
                if (active(b))
                {
                    b.SetResourceReference(BackgroundProperty, "SegOn");
                    b.SetResourceReference(ForegroundProperty, "Text");
                }
                else
                {
                    b.ClearValue(BackgroundProperty);
                    b.ClearValue(ForegroundProperty);
                }
            }
        }

        // ───────────── kaydet / başlat ─────────────

        bool Commit()
        {
            bool clean = CleanBox.IsChecked == true && _choices.Any(c => c.IsChecked);
            if (!clean && UpdatesBox.IsChecked != true && DriversBox.IsChecked != true && OptimizeBox.IsChecked != true)
            {
                KahyaDialog.Show(this, T("En az bir adım seç."), "Kahya", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            Settings.MaintenanceClean = CleanBox.IsChecked == true;
            Settings.MaintenanceItems = new HashSet<string>(_choices.Where(c => c.IsChecked).Select(c => c.Item.Id));
            Settings.MaintenanceUpdates = UpdatesBox.IsChecked == true;
            Settings.MaintenanceDrivers = DriversBox.IsChecked == true;
            Settings.MaintenanceOptimize = OptimizeBox.IsChecked == true;
            Settings.RestorePointBeforeUpdates = RestorePointBox.IsChecked == true;
            Settings.ScheduleMode = _mode;
            Settings.ScheduleDay = _day;
            Settings.ScheduleHour = _hour;
            Saved = true;
            return true;
        }

        void Save_Click(object sender, RoutedEventArgs e)
        {
            if (Commit()) DialogResult = false;
        }

        void Start_Click(object sender, RoutedEventArgs e)
        {
            if (Commit()) DialogResult = true;
        }
    }
}
