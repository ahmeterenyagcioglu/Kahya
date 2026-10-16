using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Gizlilik sekmesi
    public partial class MainWindow
    {
        List<PrivacyTweak> _privacy = new List<PrivacyTweak>();
        bool _privacyLoaded;
        bool _privacyReverting;

        void InitPrivacy() { }

        void LoadPrivacy()
        {
            foreach (var p in _privacy) p.PropertyChanged -= Privacy_PropertyChanged;
            _privacy = Privacy.Load();
            foreach (var p in _privacy) p.PropertyChanged += Privacy_PropertyChanged;
            var view = new System.Windows.Data.ListCollectionView(_privacy);
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(PrivacyTweak.Section)));
            PrivacyList.ItemsSource = view;
            _privacyLoaded = true;
            UpdatePrivacyInfo();
        }

        void PrivacyUndo_Click(object sender, RoutedEventArgs e)
        {
            if (!Confirm(T("Gizlilik sekmesinde yaptığın bütün değişiklikler, Kahya'dan önceki hallerine döndürülecek. Devam edilsin mi?")))
                return;
            bool explorer = _privacy.Any(t => t.IsApplied && t.RestartsExplorer);
            try
            {
                int n = Privacy.RestoreAll();
                Log("↩ " + T("{0} gizlilik değeri eski haline döndürüldü.", n));
            }
            catch (Exception ex)
            {
                // Bilgisayar geneli (HKLM) değerler yönetici ister; o durumda kalanlar yedekte durur.
                Log("! " + T("Bazı ayarlar geri alınamadı: {0}", ex.Message));
            }
            LoadPrivacy();
            UpdateHeader();
            if (explorer &&
                Confirm(T("Bazı değişikliklerin hemen görünmesi için Gezgin'in yeniden başlatılması gerekiyor (açık Gezgin pencereleri kapanır). Şimdi yeniden başlatılsın mı?")))
                RestartExplorer_Click(null, null);
        }

        void Privacy_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PrivacyTweak.IsApplied) || _privacyReverting) return;
            var t = (PrivacyTweak)sender;
            try
            {
                Privacy.Apply(t, t.IsApplied);
                Log(t.IsApplied ? "🛡 " + T("Uygulandı: {0}", t.Name) : "↩ " + T("Windows varsayılanına döndü: {0}", t.Name));
                if (t.RestartsExplorer)
                    Log("   " + T("Başlat menüsü / Gezgin'de görmek için 'Gezgin'i yeniden başlat'a bas ya da oturumu kapatıp aç."));
            }
            catch (Exception ex)
            {
                _privacyReverting = true;
                t.IsApplied = !t.IsApplied;
                _privacyReverting = false;
                Log("! " + T("{0} değiştirilemedi: {1}", t.Name, ex.Message));
            }
            UpdatePrivacyInfo();
            UpdateHeader();
        }

        void UpdatePrivacyInfo()
        {
            PrivacyUndoBtn.Visibility = Privacy.HasBackup ? Visibility.Visible : Visibility.Collapsed;
            int applied = _privacy.Count(p => p.IsApplied);
            int recommendedLeft = _privacy.Count(p => p.Recommended && !p.IsApplied);
            PrivacyInfo.Text = recommendedLeft == 0
                ? T("{0}/{1} ayar uygulanmış · önerilenlerin hepsi açık ✔", applied, _privacy.Count)
                : T("{0}/{1} ayar uygulanmış · {2} önerilen ayar kapalı", applied, _privacy.Count, recommendedLeft);
        }

        void PrivacyRecommended_Click(object sender, RoutedEventArgs e)
        {
            var todo = _privacy.Where(p => p.Recommended && !p.IsApplied && !p.Locked).ToList();
            if (todo.Count == 0)
            {
                Info(T("Önerilen ayarların hepsi zaten uygulanmış ✔"));
                return;
            }
            if (!Confirm(T("Şu {0} ayar uygulanacak:\n\n{1}\n\nHepsi bu sekmeden tek tek geri alınabilir. Devam edilsin mi?",
                           todo.Count, string.Join("\n", todo.Select(t => "• " + t.Name)))))
                return;
            foreach (var t in todo)
                t.IsApplied = true; // PropertyChanged uygular ve kaydeder
            if (todo.Any(t => t.RestartsExplorer) &&
                Confirm(T("Bazı değişikliklerin hemen görünmesi için Gezgin'in yeniden başlatılması gerekiyor (açık Gezgin pencereleri kapanır). Şimdi yeniden başlatılsın mı?")))
                RestartExplorer_Click(null, null);
        }

        async void RestartExplorer_Click(object sender, RoutedEventArgs e)
        {
            Log(T("Gezgin yeniden başlatılıyor…"));
            await Task.Run(() => Privacy.RestartExplorer());
            Log(T("Gezgin yeniden başlatıldı."));
        }
    }
}
