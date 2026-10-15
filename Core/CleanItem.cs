using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Kahya.Core
{
    /// <summary>Bir klasör deseninin içeriğini hedefler. Desendeki ortam değişkenleri ve '*' içeren klasör adları genişletilir.</summary>
    public sealed class PathRule
    {
        public string Pattern { get; }
        /// <summary>';' ile ayrılmış dosya maskeleri, örn. "*.log;*.cab".</summary>
        public string Masks { get; }
        public bool Recursive { get; }

        public PathRule(string pattern, string masks = "*", bool recursive = true)
        {
            Pattern = pattern;
            Masks = masks;
            Recursive = recursive;
        }
    }

    public sealed class CleanItem : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Group { get; set; }
        /// <summary>Grubun çevrilmemiş adı (Catalog.GroupOrder'daki).</summary>
        public string GroupKey { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>"Önerilenler" seçiminde ve ilk açılışta işaretli gelir.</summary>
        public bool Recommended { get; set; }

        /// <summary>"Son 24 saatte değişen dosyaları atla" seçeneği bu öğeye uygulanır.</summary>
        public bool HonorsRecentFilter { get; set; }

        /// <summary>Kişisel veriyi siler (çerez, geçmiş). "Tümü" ile seçilmez, sadece tek tek işaretlenir.</summary>
        public bool Sensitive { get; set; }

        /// <summary>Kurallarla çalışır; temizlik öncesi klasör klasör önizlenebilir.</summary>
        public bool HasPreview => Rules.Count > 0;

        public List<PathRule> Rules { get; } = new List<PathRule>();

        /// <summary>Temizlik sırasında durdurulup sonra yeniden başlatılacak servisler.</summary>
        public string[] StopServices { get; set; } = new string[0];

        /// <summary>Açıksa kullanıcının uyarılacağı işlem adları (uzantısız).</summary>
        public string[] Processes { get; set; } = new string[0];

        /// <summary>Processes'ten biri açıksa uyarmakla kalmaz, öğeyi hiç temizlemez (açık projenin önbelleği bozulmasın).</summary>
        public bool RequiresClosed { get; set; }

        /// <summary>Özel boyut ölçümü. Yoksa kurallardan hesaplanır.</summary>
        public Func<long> Measure { get; set; }

        /// <summary>
        /// Kurallardan önce çalışan özel işlem. Boşaltılan bayt sayısını döndürür;
        /// bilinmiyorsa -1 döndürür ve disk boş alanındaki değişim kullanılır.
        /// </summary>
        public Func<CleanContext, long> Action { get; set; }

        /// <summary>Yönetici izni olmadan çalışmaz (Windows klasörleri, servisler, Windows araçları).</summary>
        public bool NeedsAdmin { get; set; }

        /// <summary>Program yönetici olarak çalışmadığı için seçilemez.</summary>
        public bool Locked => NeedsAdmin && !Elevation.IsAdmin;

        public bool IsAvailable()
        {
            if (Action != null || Measure != null)
                return true;
            return Rules.Any(r => FileCleaner.ResolveDirectories(r.Pattern).Any());
        }

        bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; Notify(nameof(IsChecked)); } }
        }

        long _size = -1;
        /// <summary>-1: henüz ölçülmedi / ölçülemez.</summary>
        public long Size
        {
            get => _size;
            set { _size = value; Notify(nameof(Size)); Notify(nameof(SizeText)); }
        }

        public string SizeText => _size >= 0 ? Format.Bytes(_size) : "—";

        string _status = "";
        public string Status
        {
            get => _status;
            set { _status = value; Notify(nameof(Status)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
