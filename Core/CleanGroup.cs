using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>
    /// Temizlik listesinde açılıp kapanan bir grup (Windows, Tarayıcılar…). Başlıkta toplu seçim kutusu,
    /// "3/5 seçili" ve grubun toplam boyutu görünür; kapalıyken öğeleri yer kaplamaz.
    /// </summary>
    public sealed class CleanGroup : INotifyPropertyChanged
    {
        public CleanGroup(string key, string name, List<CleanItem> items)
        {
            Key = key;
            Name = name;
            Items = items;
            foreach (var item in items)
                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != nameof(CleanItem.IsChecked) && e.PropertyName != nameof(CleanItem.Size)) return;
                    // Boyutlar tarama sırasında arka planda değişir; görünür liste sadece arayüz iş parçacığında güncellenir.
                    var ui = System.Windows.Application.Current?.Dispatcher;
                    if (ui != null && !ui.CheckAccess()) ui.BeginInvoke(new System.Action(Refresh));
                    else Refresh();
                };
            _visible = Compute();
        }

        /// <summary>Çevrilmemiş grup adı; açık/kapalı durumu ayarlarda bununla saklanır.</summary>
        public string Key { get; }
        public string Name { get; }
        public List<CleanItem> Items { get; }

        bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded != value) { _isExpanded = value; Notify(nameof(IsExpanded)); } }
        }

        bool _hideEmpty;
        /// <summary>Ölçülüp boş çıkan (0 bayt) öğeleri gizle. Ölçülemeyenler (Windows araçları, kilitliler) hep görünür.</summary>
        public bool HideEmpty
        {
            get => _hideEmpty;
            set { if (_hideEmpty != value) { _hideEmpty = value; Refresh(); } }
        }

        List<CleanItem> _visible;
        public List<CleanItem> VisibleItems => _visible;
        public bool HasVisible => _visible.Count > 0;

        /// <summary>Gruptaki her şey yönetici istiyorsa ve program yetkisizse toplu seçim kutusu kapalı.</summary>
        public bool CanSelect => Items.Any(i => !i.Locked);

        /// <summary>true: seçilebilenlerin hepsi seçili (kişisel veriler hariç), false: hiçbiri, null: bir kısmı.</summary>
        public bool? IsChecked
        {
            get
            {
                var open = Items.Where(i => !i.Locked).ToList();
                if (!open.Any(i => i.IsChecked)) return false;
                return open.Where(i => !i.Sensitive).All(i => i.IsChecked) ? true : (bool?)null;
            }
            set
            {
                // Kutunun önerdiği değere bakılmaz: tamamı seçili değilse hepsini seç, seçiliyse hepsini kaldır.
                bool select = IsChecked != true;
                foreach (var item in Items.Where(i => !i.Locked))
                    item.IsChecked = select && !item.Sensitive;
                Notify(nameof(IsChecked));
            }
        }

        public string SelectionText
        {
            get
            {
                int count = Items.Count(i => !i.Locked);
                int selected = Items.Count(i => i.IsChecked);
                int hidden = Items.Count - _visible.Count;
                return (count == 0 ? T("yönetici gerekli") : T("{0}/{1} seçili", selected, count)) +
                       (hidden > 0 ? " · " + T("{0} boş öğe gizli", hidden) : "");
            }
        }

        public string SizeText
        {
            get
            {
                long total = Items.Where(i => i.Size > 0).Sum(i => i.Size);
                return Items.Any(i => i.Size >= 0) ? Format.Bytes(total) : "—";
            }
        }

        List<CleanItem> Compute() =>
            _hideEmpty ? Items.Where(i => i.Size != 0).ToList() : Items.ToList();

        void Refresh()
        {
            var visible = Compute();
            if (!visible.SequenceEqual(_visible))
            {
                _visible = visible;
                Notify(nameof(VisibleItems));
                Notify(nameof(HasVisible));
            }
            Notify(nameof(IsChecked));
            Notify(nameof(SelectionText));
            Notify(nameof(SizeText));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
