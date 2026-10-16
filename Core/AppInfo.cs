using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class ReleaseNote
    {
        public string Version { get; set; }
        public string Date { get; set; }
        public string Notes { get; set; }
        public string Header => $"v{Version}  ·  {Date}";
    }

    public static class AppInfo
    {
        public const string Author = "Ahmet Eren Yağcıoğlu";

        /// <summary>Kahya.csproj içindeki &lt;Version&gt; değeri.</summary>
        public static string Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        /// <summary>
        /// En yeni sürüm en üstte. Sürüm yalnızca yayınlarken yükseltilir (GitHub'a release atınca):
        /// hata düzeltmesi → 1.0.1, yeni özellik → 1.1.0. Yayınlanmamış değişiklikler en üstteki notta birikir.
        /// </summary>
        public static IReadOnlyList<ReleaseNote> ReleaseNotes => new[]
        {
            new ReleaseNote
            {
                Version = "1.8.5", Date = T("16 Ekim 2026"),
                Notes = T("• Birden fazla ağ kartı olan bilgisayarlarda alınan yedekler geri yüklenemiyordu; düzeldi\n• Sade menü: 8 bölüm, alt sekmeler (Temizlik, Güncellemeler, Uygulamalar, Windows ayarları, Sistem, Yedekleme)\n• Kısa ve anlaşılır metinler\n• Program kur: Brave, Zoom gibi kurulu programlar artık tanınıyor\n• Sistem › Durum'da disk alanı; DNS satırındaki boşluk giderildi"),
            },
            new ReleaseNote
            {
                Version = "1.8.1", Date = T("16 Ekim 2026"),
                Notes = T("• Araçlar › Ağ araçları › DNS sunucusu: Özel seçeneğiyle kendi DNS sunucunu (birincil ve ikincil) elle girebilirsin\n• Araçlar sayfasında sütunlar dengeli, büyük boşluk kalmıyor; DNS satırı daha derli toplu"),
            },
            new ReleaseNote
            {
                Version = "1.8.0", Date = T("16 Ekim 2026"),
                Notes = T("• Otomatik yedek: kişisel klasörler, Wi-Fi ağları, program listesi ve Windows ayarları seçtiğin sıklıkta yedeklenir (Format yardımcısı › Otomatik yedek). En yeni 3 otomatik yedek tutulur\n• Yedeğe göz at: .kahya yedeğinde gezip arayarak tek bir dosyayı ya da klasörü istediğin yere geri al; var olanların üzerine yazmaz\n• Disk sorunlanınca ya da SSD aşınması %90'ı geçince bildirim alanından uyarı; tıklayınca Format yardımcısı açılır\n• Araçlar › Ağ araçları: DNS sunucusu seçimi (Cloudflare, Google, Quad9, AdGuard ya da Otomatik) ve eski hali yedeklenerek hosts düzenleme\n• Format öncesi kontrol listesinde her tarayıcının şifre sayfasını açan düğmeler (dışa aktarmak için)\n• Kahya'nın temasına uyan soru pencereleri; yer açmayan işlerde '0 B boşaltıldı' yerine 'yapıldı'; Gizlilik geri alınınca Gezgin'i yeniden başlatmayı soruyor"),
            },
            new ReleaseNote
            {
                Version = "1.7.1", Date = T("16 Ekim 2026"),
                Notes = T("• Format yedeği Masaüstü'ne alınınca kendini de kopyalamaya çalışıp hiç bitmiyordu; düzeldi\n• Çok derin klasörlerdeki (260 karakteri aşan yollar) dosyalar, gizli ve salt okunur dosyaların öznitelikleri ve boş klasörler artık yedeğe girip geri geliyor; yedeğe giremeyen dosya olursa söyleniyor\n• Bazı programlar (ör. InstallShield ile kurulanlar) Uygulamalar'dan kaldırılamıyordu; düzeldi\n• WinSxS analizinin sonucu anlaşılır bir özet penceresinde; temizlik öneriliyorsa Derin Temizlik'te seçili gelir\n• Pencere küçük ekranlarda ve yüksek ölçeklemede ekrana sığıyor\n• Windows Update, disk sağlığı ve ağ hatalarında anlaşılır mesajlar; simgesi olmayan programlara genel simge"),
            },
            new ReleaseNote
            {
                Version = "1.7.0", Date = T("16 Ekim 2026"),
                Notes = T("• Program kaldırınca geride kalanlar: kaldırıcının silmediği klasörler ve kayıt defteri anahtarları bulunur, istediklerini silersin. Klasörler Geri Dönüşüm Kutusu'na gider, anahtarlar önce yedeklenir\n• Gizlilik › Windows ayarları: yeni Görünüm ve kullanım bölümü (dosya uzantıları, gizli dosyalar, Gezgin'i Bu bilgisayar'da açma; Windows 11'de klasik sağ tık menüsü, Copilot ve Pencere Öğeleri'ni kapatma, görev çubuğunda Görevi sonlandır) ve Recall'u kapatma\n• Araçlar › WinSxS boyutunu analiz et; Derin Temizlik'te eski güncellemeleri kalıcı silme (DISM /ResetBase)"),
            },
            new ReleaseNote
            {
                Version = "1.6.0", Date = T("16 Ekim 2026"),
                Notes = T("• Başka dillere çeviri: diller artık dil dosyası (XML). Hakkında › Ayarlar › Dil dosyaları… klasörü açar; içindeki lang-sample.xml'i kopyalayıp (ör. de.xml) çeviren kendi dilini ekler, dil listesinde kendiliğinden çıkar. Nasıl yapılacağı dosyanın başında Türkçe ve İngilizce yazıyor\n• Format sonrası asistanı artık her açılışta gelmiyor: sadece başlayıp yarıda kesilen bir geri yükleme varsa açılır. \"Kaldığın yerden devam et\" şeridinde Vazgeç düğmesi var"),
            },
            new ReleaseNote
            {
                Version = "1.5.0", Date = T("16 Ekim 2026"),
                Notes = T("• Formattan önce kontrol listesi: tarayıcı şifreleri, OneDrive, iki adımlı doğrulama, Office/Adobe lisansları, oyun hesapları (bu bilgisayarda olanlara göre)\n• BitLocker ile şifreli disklerin kurtarma anahtarları yedeğe eklenir\n• Windows ayarları yedeklenir ve geri yüklenir: duvar kâğıdı, koyu/açık tema ve renkler, görev çubuğu, Gezgin, fare, klavye düzenleri\n• Yedek bitince doğrulanır: paket baştan sona okunur, bozuk USB ya da yarım kopya formattan önce fark edilir\n• Yeni kurulmuş bir Windows'ta yedek bulunursa format sonrası asistanı kendiliğinden önerilir\n• Format sonrası yedeğin yanındaki Kahya açılınca: kendini bilgisayara kopyalar (Başlat menüsü ve masaüstü kısayoluyla) ve doğrudan asistanla açılır; USB sonra çıkarılabilir\n• Format USB'si hazırlama: Microsoft'un Medya Oluşturma Aracı ya da Rufus tek tıkla"),
            },
            new ReleaseNote
            {
                Version = "1.4.0", Date = T("16 Ekim 2026"),
                Notes = T("• Format sonrası asistanı, adım adım: yedeği kendisi bulur (bulamazsa manuel seçilir) → Wi-Fi ağlarını ekler, internet yoksa ağ sürücüsünü kurar → kurulacak programlar → dosyalar ve kayıtlar (her kategoride tek tek seçim) → ekran kartı → kurulum. Önce programlar kurulur, oyun kayıtları en sonda\n• winget'te olmayan programların listesi masaüstüne kaydedilebilir\n• Gerekli bileşenler tek tıkla: Visual C++ 2005–2022, DirectX, .NET 8/10 (asistanda ve Uygulamalar › Program kur'da)\n• Aynı yedek tekrar açılınca önceden geri yüklenenler \"geri yüklendi\" notuyla işaretsiz gelir\n• Asistan kapanırsa ya da Kahya yeniden başlarsa kaldığı yerden devam eder\n• Format yedeği artık bir .kahya paketi ve yanında Kahya.exe: soru yok; paketin kendi simgesi var, çift tıklayınca asistan açılır. İçi ZIP'tir, 7-Zip ve WinRAR açar\n• Her zaman yönetici olarak açılır (Windows bir kez izin sorar); Hakkında › Ayarlar'dan kapatılabilir\n• Gerçek ağ kartı (Wi-Fi/Ethernet) sürücüleri her zaman yedeklenir; Tailscale, VPN ve sanal makine kartları buna girmez (programları kurulunca gelirler)\n• Belgeler gibi klasörlerin boyutu yanlış hesaplanıyordu; düzeldi. Klasörlerin içinde kalan oyun/ayar klasörleri artık iki kez yedeklenmiyor\n• Visual C++, .NET gibi bileşenler program listesine girmiyor; zaten kurulu programlar asistanda kendiliğinden atlanır"),
            },
            new ReleaseNote
            {
                Version = "1.3.1", Date = T("16 Ekim 2026"),
                Notes = T("• Sıkıştırılan yedekte Masaüstü, Belgeler gibi klasörlerin boş kopyaları geride kalıyordu; artık temizleniyor\n• .kahya yedek dosyalarının kendi simgesi var (belge + Kahya rozeti); Kahya.exe ile karışmıyor"),
            },
            new ReleaseNote
            {
                Version = "1.3.0", Date = T("16 Ekim 2026"),
                Notes = T("• Format yedeği bitince tamamı tek bir arşive (7z ya da ZIP) sıkıştırılır; sürücüler, Wi-Fi ve Kahya dışarıda kalır ki format sonrası internet yokken de kurulabilsin\n• Yedeğin içine Kahya.exe de konur: format sonrası indirmeye gerek yok\n• 7-Zip kurulu değilse arşiv Windows'un kendi aracıyla açılır\n• Daha çok oyun: Steam kayıtları, Unreal Engine (Epic) oyunları, Ubisoft Connect ve Rockstar oyunları\n• Font yedeğinde Microsoft Office'in fontları artık listelenmiyor (Office kurulunca geri gelirler)"),
            },
            new ReleaseNote
            {
                Version = "1.2.0", Date = T("16 Ekim 2026"),
                Notes = T("• Format yedeği artık bir .kahya dosyası içeriyor: format sonrası 'Yedekten geri yükle' (ya da dosyaya çift tıklama) her şeyi sırayla geri yükler — sürücüler, Wi-Fi, fontlar, kişisel klasörler, programlar, ayarlar ve oyun kayıtları\n• Oyun kayıtları yedeği: Minecraft (dünyalar, modlar, kaynak/gölgelendirici paketleri; CurseForge, Prism, Modrinth ve Bedrock dahil), Kayıtlı Oyunlar, My Games ve Unity oyunları\n• Genel bakışta açılış süresi: son açılış, 30 günlük ortalamaya göre değişim ve açılışı en çok yavaşlatanlar\n• Hata bildir: hata penceresinden ya da Hakkında'dan hazır doldurulmuş bir hata bildirimi açılır"),
            },
            new ReleaseNote
            {
                Version = "1.1.0", Date = T("16 Ekim 2026"),
                Notes = T("• winget yoksa Kahya kurmayı teklif eder ve tek tıkla kurar\n• Format yedeğinde 7z seçilince 7-Zip yoksa tek tıkla kurulur\n• .NET Framework 4.8 eksikse açılışta söylenir ve indirme sayfası açılır"),
            },
            new ReleaseNote
            {
                Version = "1.0.1", Date = T("16 Ekim 2026"),
                Notes = T("• Beklenmeyen hatalar artık errors.log dosyasına kaydediliyor; aynı hata üst üste pencere açmıyor\n• Kendini güncelleme etkin: yeni sürümler GitHub'dan kontrol edilip tek tıkla kuruluyor"),
            },
            new ReleaseNote
            {
                Version = "1.0.0", Date = T("11 Ekim 2026"),
                Notes = T("• Genel bakış: disk alanı, güncellemeler, son bakım, disk ve pil sağlığı tek sayfada\n• Temizlik: Temp, Prefetch, Windows Update önbelleği, tarayıcılar ve 30'dan fazla kategori; önizleme ve temizlik geçmişi\n• Güncellemeler: winget ile uygulama güncellemeleri, başarısız olanın nedeni, istemediğini atlama, öncesinde geri yükleme noktası\n• Tek tıkla bakım: temizlik + güncellemeler + eski sürücüler; isteğe göre otomatik (her gün / hafta / iki haftada bir), disk dolarken uyarır\n• Uygulamalar: program kaldırma, gereksiz Windows uygulamaları, toplu program kurma; yüklü program listesini kaydedip format sonrası winget'te olanları tek seferde kurma\n• Başlangıç: açılışta başlayan uygulamalar, zamanlanmış görevler ve servisler\n• Gizlilik: reklam kimliği, Başlat önerileri, kilit ekranı reklamları, Bing araması vb.; tek tıkla geri alma\n• Disk analizi: büyük klasörler, dosya türleri, büyük/eski dosyalar, kopya dosyalar, boş klasörler\n• Araçlar: geri yükleme noktaları, disk ve pil sağlığı, SFC/DISM/CHKDSK, ağ araçları, ekran kartı sürücüsü kontrolü, sistem bilgisi, Windows Update durumu ve erteleme, güç planı ve performans ayarları\n• Format yardımcısı: program listesi, Wi-Fi şifreleri, ürün anahtarı, seçilen sürücüler (klasör / ZIP / 7z) ve kişisel klasörler tek seferde yedeklenir; format sonrası geri yüklenir\n• Bildirim alanında çalışma: disk dolunca, uygulama güncellemesi ya da yeni ekran kartı sürücüsü çıkınca bildirim; Windows açılınca başlayabilir\n• Tarayıcı eklentileri denetimi ve tek sayfalık bilgisayar durum raporu\n• Normal kullanıcı olarak açılır; yönetici isteyen özellikler kalkanla işaretli, tek tıkla yönetici olarak yeniden başlatılır\n• Türkçe/İngilizce, koyu/açık tema (Windows'a uyabilir), ayarları dışa/içe aktarma, kendini güncelleme; taşınabilir (veriler programın yanındaki Data klasöründe)"),
            },
        };
        // ───────────── bu bilgisayar ─────────────

        /// <summary>systeminfo benzeri özet. WMI sorguları yaptığı için arka planda çağrılmalı.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> SystemSummary()
        {
            var (current, first) = InstallDates();
            var installText = current.HasValue ? DateWithAge(current.Value) : null;
            // Büyük sürüm yükseltmeleri (ör. 23H2 → 24H2) kurulum tarihini sıfırlar; ilk kurulum Setup\Source OS kayıtlarında kalır.
            if (first.HasValue && current.HasValue && first.Value < current.Value.AddDays(-1))
                installText += Environment.NewLine + T("İlk kurulum: {0} (sonra yükseltildi)", DateWithAge(first.Value));

            var boot = DateTime.Now - TimeSpan.FromMilliseconds(GetTickCount64());
            var model = string.Join(" ", Wmi("SELECT Manufacturer, Model FROM Win32_ComputerSystem", "Manufacturer", "Model"));
            var board = string.Join(" ", Wmi("SELECT Manufacturer, Product FROM Win32_BaseBoard", "Manufacturer", "Product"));
            var bios = string.Join(" ", Wmi("SELECT Manufacturer, SMBIOSBIOSVersion FROM Win32_BIOS", "Manufacturer", "SMBIOSBIOSVersion"));
            var biosDate = WmiDate("SELECT ReleaseDate FROM Win32_BIOS", "ReleaseDate");
            if (biosDate.HasValue) bios += T(" ({0:dd.MM.yyyy})", biosDate.Value);

            var gpus = new List<string>();
            try
            {
                foreach (var g in GpuDrivers.Installed())
                    gpus.Add(T("{0} (sürücü {1})", g.Name, g.Version));
            }
            catch { }

            var disks = new List<string>();
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher("SELECT Model, Size FROM Win32_DiskDrive"))
                using (var items = s.Get())
                    foreach (System.Management.ManagementBaseObject o in items)
                        disks.Add($"{(o["Model"] as string ?? "").Trim()} ({Format.Bytes(Convert.ToInt64(o["Size"] ?? 0L))})");
            }
            catch { }

            var list = new List<KeyValuePair<string, string>>
            {
                Pair(T("Bilgisayar"), Environment.MachineName + (model.Length > 0 ? " · " + model : "")),
                Pair(T("Windows"), WindowsVersion()),
                Pair(T("Windows kurulumu"), installText),
                Pair(T("Son açılış"), T("{0:dd.MM.yyyy HH:mm} ({1} önce)", boot, Uptime())),
                Pair(T("İşlemci"), Registry(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString")),
                Pair(T("Bellek (RAM)"), Format.Bytes(TotalMemory())),
                Pair(T("Ekran kartı"), string.Join(Environment.NewLine, gpus)),
                Pair(T("Diskler"), string.Join(Environment.NewLine, disks)),
                Pair(T("Anakart"), board),
                Pair(T("BIOS"), bios),
                Pair("Kahya", "v" + Version),
            };
            return list.Where(p => !string.IsNullOrWhiteSpace(p.Value)).ToList();
        }

        static string DateWithAge(DateTime d)
        {
            var days = (int)(DateTime.Now - d).TotalDays;
            string age = days >= 365 ? T("{0:0.#} yıl önce", days / 365.0)
                       : days >= 60 ? T("{0} ay önce", days / 30)
                       : T("{0} gün önce", days);
            return $"{d:dd.MM.yyyy} ({age})";
        }

        /// <summary>Şu anki Windows'un kurulum tarihi ve (varsa) yükseltmelerden önceki ilk kurulum tarihi.</summary>
        public static (DateTime? current, DateTime? first) InstallDates()
        {
            DateTime? FromUnix(object v) =>
                v is int i && i > 0 ? DateTimeOffset.FromUnixTimeSeconds((uint)i).LocalDateTime : (DateTime?)null;

            DateTime? current = null, first = null;
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (var k = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                        current = FromUnix(k?.GetValue("InstallDate"));
                    first = current;
                    using (var setup = hklm.OpenSubKey(@"SYSTEM\Setup"))
                        foreach (var name in setup?.GetSubKeyNames() ?? new string[0])
                        {
                            if (!name.StartsWith("Source OS", StringComparison.OrdinalIgnoreCase)) continue;
                            using (var k = setup.OpenSubKey(name))
                            {
                                var d = FromUnix(k?.GetValue("InstallDate"));
                                if (d.HasValue && (first == null || d < first)) first = d;
                            }
                        }
                }
            }
            catch { }
            return (current, first);
        }

        static KeyValuePair<string, string> Pair(string k, string v) => new KeyValuePair<string, string>(k, v?.Trim());

        public static string WindowsVersion()
        {
            const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            var name = Registry(key, "ProductName") ?? "Windows";
            var display = Registry(key, "DisplayVersion");
            var build = Registry(key, "CurrentBuild");
            var ubr = Registry(key, "UBR");
            // ProductName, Windows 11'de de "Windows 10" yazar; 22000 ve üstü derlemeler Windows 11'dir.
            if (int.TryParse(build, out var b) && b >= 22000)
                name = name.Replace("Windows 10", "Windows 11");
            return T("{0} {1} (derleme {2})", name, display, build + (ubr != null ? "." + ubr : ""));
        }

        static string Registry(string path, string value)
        {
            try
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(path))
                    return k?.GetValue(value)?.ToString();
            }
            catch { return null; }
        }

        /// <summary>Her nesne için verilen özelliklerin boş olmayanlarını boşlukla birleştirir.</summary>
        static IEnumerable<string> Wmi(string query, params string[] properties)
        {
            var result = new List<string>();
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher(query))
                using (var items = s.Get())
                    foreach (System.Management.ManagementBaseObject o in items)
                    {
                        var v = string.Join(" ", properties.Select(p => (o[p] as string)?.Trim())
                                                           .Where(x => !string.IsNullOrEmpty(x) && !x.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase)));
                        if (v.Length > 0) result.Add(v);
                    }
            }
            catch { }
            return result;
        }

        static DateTime? WmiDate(string query, string property)
        {
            var raw = Wmi(query, property).FirstOrDefault();
            try { return raw != null ? System.Management.ManagementDateTimeConverter.ToDateTime(raw) : (DateTime?)null; }
            catch { return null; }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();

        static long TotalMemory()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            return GlobalMemoryStatusEx(ref m) ? (long)m.ullTotalPhys : -1;
        }

        /// <summary>Bilgisayar ne zamandır açık (uyku/hazırda bekletme süreleri dahil).</summary>
        public static TimeSpan UptimeSpan => TimeSpan.FromMilliseconds(GetTickCount64());

        public static string Uptime()
        {
            var t = UptimeSpan;
            return t.TotalDays >= 1 ? T("{0} gün {1} saat", (int)t.TotalDays, t.Hours)
                 : t.Hours >= 1 ? T("{0} saat {1} dakika", t.Hours, t.Minutes)
                 : T("{0} dakika", t.Minutes);
        }
    }
}
