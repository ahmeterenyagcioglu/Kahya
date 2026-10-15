# Kahya

**v1.0.1** · Geliştiren: Ahmet Eren Yağcıoğlu

> Eski adı **WinCleaner**. Yeni `Kahya.exe` eskisinin yanına konursa `Data` klasörü (ayarlar, geçmiş) aynen kullanılır;
> eski "WinCleaner Haftalik" otomatik bakım görevi ilk yönetici açılışında "Kahya Bakim" adıyla yeniden kurulur.

> Sürüm yalnızca yayınlarken (GitHub release) yükseltilir: hata düzeltmesi → `1.0.1`, yeni özellik → `1.1.0`.
> `Kahya.csproj` içindeki `<Version>` değerini değiştir ve `Core/AppInfo.cs` içindeki `ReleaseNotes` listesine not ekle;
> yayınlanmamış değişiklikler en üstteki notta birikir. Hakkında sayfası ve exe özellikleri bunu otomatik gösterir.

Temp, Prefetch, cleanmgr vb. işlemleri tek tek yapmak yerine tek tıkla yapan küçük bir Windows temizleyici.

- Tek dosya: `Kahya.exe`. .NET Framework 4.8 kullanır; Windows 10/11'de zaten yüklü, ekstra kurulum yok.
- Normal kullanıcı olarak açılır, UAC sormaz. Yönetici isteyen özellikler (Windows\Temp, Prefetch, Update önbelleği,
  SFC/DISM, sürücüler, tek tıkla bakım, geri yükleme noktaları…) kalkan simgesiyle işaretli ve kilitli gelir; üstteki
  şeritten ya da kilitli bir butona basınca **Yönetici olarak yeniden başlat** ile açılır (kalınan sayfaya döner).
  Zamanlanmış otomatik bakım her zaman yönetici olarak çalışır.
- Açılınca her şeyi tarar, kategori başına ne kadar yer açılacağını gösterir. **Temizle** ile işaretli olanları siler.
- Programın tüm verileri (ayarlar, geçmiş, disk önbelleği, son otomatik bakım kaydı) exe'nin yanındaki `Data` klasöründe durur.
  Program taşınabilirdir: klasörü kopyalamak ya da silmek yeterli, Windows'ta iz bırakmaz.
  Oraya yazılamazsa `%APPDATA%\Kahya` kullanılır. Eski sürümlerin verileri ilk açılışta otomatik taşınır.

## Derleme

```powershell
dotnet build -c Release
# çıktı: bin\Release\net48\Kahya.exe  → arkadaşlarına sadece bu dosyayı gönder
```

## Neler temizleniyor

| Grup | Kategori | Varsayılan |
|---|---|---|
| Windows | Kullanıcı Temp (`%TEMP%`) | ✔ |
| | Windows Temp (`C:\Windows\Temp`, `SystemTemp`) | ✔ |
| | Prefetch | ✔ |
| | Geri Dönüşüm Kutusu | ✔ |
| | Windows Update indirme önbelleği (servis durdurulup tekrar başlatılır) | ✔ |
| | Teslim İyileştirme (Delivery Optimization) önbelleği | ✔ |
| | INetCache | ✔ |
| | Küçük resim / simge önbelleği | ✔ |
| | DirectX shader önbelleği (D3DSCache) | ✔ |
| | Windows Hata Raporları (WER) | ✔ |
| | Çökme dökümleri (`MEMORY.DMP`, Minidump, CrashDumps…) | ✔ |
| | Windows log dosyaları (CBS, DISM, Panther…) | ✔ |
| | Son kullanılan dosyalar listesi | – |
| | DNS önbelleği (`ipconfig /flushdns`) | ✔ |
| Tarayıcılar | Chrome, Edge, Brave, Vivaldi, Opera/GX, Firefox (**sadece önbellek**; şifre, geçmiş ve çerezler kalır) | ✔ |
| | Tarayıcı çerezleri (tüm sitelerden çıkış yapılır) | – |
| | Tarayıcı geçmişi (Chromium tabanlılar; Firefox'ta geçmiş yer imleriyle aynı dosyada olduğu için dokunulmaz) | – |
| Uygulamalar | Discord, Teams, Spotify (çevrimdışı şarkılar hariç) | ✔ |
| Video & Fotoğraf | Adobe Premiere Pro / After Effects Media Cache ve AE disk önbelleği (özel konum da bulunur; projeler ve otomatik kayıtlar kalır) | – |
| | Lightroom / Camera Raw önbelleği (fotoğraflar, Lightroom yerel kitaplığı, katalog ve ön ayarlar kalır) | – |
| | Photoshop çalışma diski artıkları (çökünce kalan `Photoshop Temp…` dosyaları; diğer disklerin kökü de) | – |
| | CapCut önbelleği (sadece efekt/dalga formu/küçük resim/önizleme klasörleri ve loglar; projelere, veritabanlarına ve bulut taslaklarına dokunulmaz) | – |
| | DaVinci Resolve logları, OBS Studio logları ve tarayıcı kaynağı önbelleği (sahneler, profiller, oturumlar kalır) | – |
| Oyunlar & Ekran kartı | Steam web önbelleği/logları | ✔ |
| | NVIDIA/AMD shader önbelleği (oyunlarda ilk açılışta kısa takılma olabilir) | – |
| | Sürücü kurulum artıkları (`C:\AMD`, `C:\NVIDIA`, …) | ✔ |
| Geliştirici | npm / Yarn / pip / NuGet önbellekleri, VS Code önbelleği | – |
| Derin | Windows Disk Temizleme (cleanmgr, tüm kategoriler; İndirilenler, Dosya Geçmişi ve Geri Dönüşüm Kutusu hariç) | ✔ |
| | DISM bileşen deposu temizliği (yavaş) | – |

Liste gruplara ayrılır; her grup başlığında toplu seçim kutusu, "3/5 seçili" ve grubun toplam boyutu görünür, tıklayınca
açılıp kapanır (hangi grupların açık olduğu hatırlanır). **Boş olanları gizle** taramada hiçbir şey bulunmayan öğeleri
listeden kaldırır. Video & Fotoğraf öğeleri, program açıksa hiç çalışmaz (açık projenin önbelleği bozulmasın diye atlanır).
Bilgisayarda bulunmayan programların kategorileri listede görünmez. Çerez ve geçmiş öğeleri **Tümü** ile de seçilmez,
sadece tek tek işaretlenir.

Satırdaki büyüteç düğmesi önizleme açar: o kategori hangi klasörden kaç dosya, ne kadar silecek. Önizleme hiçbir şey silmez.

Seçimlerini **Hakkında → Ayarları dışa aktar** ile bir `.ini` dosyasına kaydedip arkadaşına verebilirsin; o da **içe aktar** der.

## Güvenlik

- Kategori klasörünün kendisi değil, **içi** silinir.
- Junction/sembolik bağlantıların hedefine asla girilmez.
- Kullanımdaki dosyalar atlanır ve log'da sayısı gösterilir.
- "Son 24 saatte değişen Temp dosyalarını atla" seçeneği o an çalışan kurulumların dosyalarını korur.

## Uygulama güncellemeleri (winget)

**Güncellemeler** sayfası açılınca `winget upgrade` ile güncellemesi olan uygulamaları listeler
(kurulu sürüm → yeni sürüm). İstediklerini seçip **Seçilenleri güncelle** dersen hepsini sırayla, sessiz kurulumla günceller.

- winget, Windows 10/11'de "Uygulama Yükleyici" (App Installer) ile gelir.
- Güncellenen program açıksa kurulum onu kapatabilir.
- winget'in kendisi (App Installer) listede hep en sona alınır.
- Bir güncelleme başarısız olursa **nedeni satırın altında Türkçe yazar** ("Program şu an açık; kapatıp tekrar dene" gibi).
  Bilinen winget hata kodları ve kurulum programı kodları (1603, 1618…) anlaşılır mesaja çevrilir; **Ayrıntı** winget'in ham çıktısını gösterir.
  İşlem kaydındaki winget satırları da (Windows dili İngilizceyse winget İngilizce yazar) Türkçeleştirilir.
- **Atla:** güncellemesini istemediğin uygulama seçili gelmez ve tek tıkla/otomatik bakımda da güncellenmez. Tekrar tıklayınca geri alınır.

**Önce geri yükleme noktası oluştur** seçiliyse (varsayılan), güncellemeden önce bir Sistem Geri Yükleme noktası alınır.
Sistem Geri Yükleme kapalıysa açmayı teklif eder. Not: geri yükleme noktası programları/sistem ayarlarını geri alır;
temizlikte silinen önbellek dosyalarını geri getirmez (zaten gerek de yok, yeniden oluşurlar).

## Format yardımcısı

Format öncesi tek ekrandan yedek: **program listesi** (yüklü programlar tek tek seçilir; winget'te olanlar format sonrası en yeni sürümüyle tek seferde kurulur),
**Wi-Fi ağları ve şifreleri** (`netsh wlan export … key=clear`), **Windows ürün anahtarı** (BIOS'taki OEM anahtar ya da
kayıttaki anahtar; dijital lisansta genel anahtar olduğu söylenir), **sürücüler** ve **kişisel klasörler** (Masaüstü,
Belgeler, İndirilenler…; robocopy ile). Sürücüler tek tek seçilir ("Önemliler" ağ/ses/Bluetooth/yonga setini seçer, ekran
kartını değil); yönetici gerektirmeden DriverStore'dan kopyalanır ve istenirse **ZIP** ya da (7-Zip kuruluysa) **7z**
yapılır. Yedek `Kahya-Yedek-<bilgisayar>-<tarih>` klasörüne, içinde ne yapılacağını anlatan `OKU-BENI.txt` ile kaydedilir.
Hedef varsayılan olarak C: dışındaki ilk disk / USB bellek; C: seçilirse uyarır.

Ayrıca:

- **Tarayıcı yer imleri:** Chrome, Edge, Brave, Vivaldi ve Opera'nın her profili, her tarayıcının içe aktarabildiği bir
  HTML dosyasına çevrilir; Firefox'un son yer imi yedeği (`.jsonlz4`) kopyalanır.
- **Fontlar:** Windows'la gelmeyen, sonradan kurulmuş fontlar (Windows'un kendi fontları TrustedInstaller'a aittir, onlar
  alınmaz). Liste açılıp tek tek seçilebilir. Geri yüklerken bu kullanıcıya kurulur (yönetici gerekmez), zaten kurulu olanlar atlanır.
- **Program ayarları:** CapCut ön ayarları, Premiere Pro profili (kısayollar, efekt ön ayarları, çalışma alanları), Adobe
  LUT'ları, Camera Raw / Lightroom Classic / Photoshop ön ayarları ve OBS sahneleri; sadece bilgisayarda olanlar listelenir.
  Yollar `{APPDATA}` gibi simgelerle kaydedildiği için farklı kullanıcı adıyla kurulan Windows'a da geri yüklenir.
  Geri yüklerken daha yeni dosyaların üzerine yazılmaz, programı açık olanlar atlanır.

Format sonrası aynı sayfadan Wi-Fi ağları, sürücüler (klasör, .zip, .7z; `pnputil /add-driver /subdirs /install`),
fontlar ve program ayarları geri yüklenir, programlar "Program kur › Listeden yükle" ile kurulur.

## Bildirim alanı

Hakkında › Ayarlar'dan açılır. Açıkken pencereyi kapatmak Kahya'yı gizler; saatin yanındaki simgeden açılır ya da Çıkış
ile kapatılır. Açılıştan birkaç dakika sonra ve sonra 6 saatte bir disk alanına, uygulama güncellemelerine, ekran kartı
sürücüsüne ve bekleyen Windows yeniden başlatmasına bakar; yeni bir şey varsa bildirim gösterir (aynı şeyi tekrarlamaz).
**Önbellek uyarısı** (Kapalı / 2 / 5 / 10 GB, varsayılan 5 GB): bir uygulama, tarayıcı ya da video/fotoğraf programının
önbelleği eşiği geçince "CapCut önbelleği 6,2 GB oldu" gibi haber verir. Kendiliğinden silmez; bildirime tıklayınca Temizlik açılır.
"Windows açılınca başlat" oturum açılışında pencere açmadan bildirim alanında başlatır (`HKCU\…\Run`, `--tray`).
Program tek kopya çalışır: tekrar açılırsa var olan pencere öne gelir.

## Genel bakış

İlk sayfa. Disk alanı, temizlenebilir alan, bekleyen uygulama güncellemeleri, son bakım, otomatik bakım, disk sağlığı,
açılışta başlayan uygulama sayısı, gizlilik önerileri ve (dizüstünde) pil sağlığı / (masaüstünde) açık kalma süresi.
Dikkat isteyenler turuncu/kırmızı görünür; her kutucuk ilgili sayfayı açar. Güncelleme kontrolü arka planda yapılır,
Güncellemeler sekmesi aynı sonucu kullanır.

## Başlangıç

- **Uygulamalar:** Windows açılışında başlayanlar (Run kayıtları + Başlangıç klasörleri); anahtarla açıp kapatırsın.
  Görev Yöneticisi'nin kullandığı yöntemin aynısıdır (`StartupApproved`): kayıt silinmez, istediğin an geri açılır.
  Exe'si artık olmayan bozuk kayıtlar "Dosya bulunamadı" diye işaretlenir. Tüm kullanıcılar için olanlar yönetici ister.
- **Zamanlanmış görevler:** programların kurduğu görevler (güncelleyiciler vb.). Microsoft'un görevleri listelenmez.
  Kapatılan görev silinmez, sadece devre dışı kalır.
- **Servisler:** Microsoft dışı servisler. Kapatmak servisi **devre dışı bırakmaz**, "Elle" başlatmaya alır:
  açılışta başlamaz ama programı gerekirse yine çalıştırabilir. Değiştirmek yönetici ister.

## Disk analizi

Bir sürücü seç; altı görünüm var:

- **Klasörler:** en çok yer kaplayan klasörler, tıklayıp içine girerek.
- **Büyük ve eski dosyalar:** sürücüdeki en büyük 1000 dosya; boyut (100 MB+ / 1 GB+) ve "dokunulmamış"
  (6 ay / 1 yıl / 2 yıldır değiştirilmemiş) süzgeçleri. Seçtiklerini Geri Dönüşüm Kutusu'na taşıyabilirsin.
- **Dosya türleri:** Videolar, Resimler, Arşivler, Oyun verisi, Programlar… gibi kategorilere göre dağılım;
  bir kategoriye tıklayınca uzantı uzantı (.mp4, .zip…) ayrıntısı.
- **Kopyalar** ve **Boş klasörler:** bir klasör seçip ararsın (aşağıda).
- **Render artıkları:** tüm disklerde (ya da seçtiğin klasörde) video/fotoğraf programlarının proje klasörlerinde
  bıraktığı önizlemeleri bulur (aşağıda).

Klasör ve tür görünümleri **hiçbir şey silmez**; klasör simgesiyle Gezgin'de açarsın, kararı sen verirsin.
Büyük dosyalarda Windows, program, AppData, oyun ve proje (`.git`, `node_modules`) klasörlerindeki dosyalar ile
`pagefile.sys` / `hiberfil.sys` gibi sistem dosyaları **işaretlenemez**.

- Sonuçlar `Data\disk-cache` altına kaydedilir; aynı sürücüye tekrar tıklayınca anında açılır.
  Güncel hali için yenile düğmesi. (v1.5'ten önceki kayıtlarda dosya türleri yoktur, bir kez yeniden tara.)
- Fiziksel bir bölüme bağlı olmayan sürücüler (Google Drive gibi) bulut simgesiyle gösterilir ve taranmadan önce sorulur.

### Hiç taranmayan yerler (kopya ve boş klasör araması)

Windows, Program Files, ProgramData, AppData, Geri Dönüşüm Kutusu, Windows.old gibi sistem klasörleri;
`WindowsApps`, `XboxGames`, `steamapps`, `SteamLibrary`, `Epic Games`, `Riot Games` gibi oyun kütüphaneleri;
`node_modules`, `.git` gibi proje klasörleri. Bunlar hangi sürücüde ve ne kadar derinde olursa olsun atlanır.
Bunlardan birini (ya da içini) klasör olarak seçersen arama yapılmaz, başka bir klasör seçmen istenir.

### Boş klasör bulucu

İçinde hiç dosya olmayan (alt klasörleri de boş olan) klasörleri bulur; iç içe boş klasörler tek satırda gösterilir.
Gizli+sistem klasörleri ve bağlantılar hiç boş sayılmaz. Silmeden hemen önce klasörün hâlâ dosyasız olduğu tekrar kontrol edilir.

### Render artıkları

Sadece adı ve yapısı birebir tutan klasörler listelenir:

- Premiere Pro: `Adobe Premiere Pro Video Previews` / `Audio Previews` (otomatik kayıtlar ve projeler bunların dışında)
- Lightroom Classic: `<Katalog> Previews.lrdata`, yanında `<Katalog>.lrcat` varsa. **Akıllı önizlemeler** (`Smart Previews.lrdata`)
  orijinaller bağlı değilken düzenleme için kullanıldığından listelenmez.
- DaVinci Resolve: `CacheClip` render önbelleği

Her satırda boyut ve **son kullanım** (içindeki en yeni dosyanın tarihi) yazar; **3 aydan eskiler** bitmiş projelerinkini seçer.
Program açıksa silinmez. Render dosyaları çok büyük olabildiği için Geri Dönüşüm Kutusu'na değil, kalıcı olarak silinir;
program o projeyi açınca gerekirse yeniden render alır. Silmeden hemen önce klasör tekrar kontrol edilir.

## Uygulamalar

- **Yüklü programlar:** "Programlar ve Özellikler" listesi; arama, boyut ve kurulum tarihi. **Kaldır** programın kendi kaldırıcısını açar.
- **Gereksiz Windows uygulamaları:** Windows ile gelen, yaygın olarak kullanılmayan Store uygulamaları (Haberler, Clipchamp,
  Solitaire, Candy Crush…). Sadece bilgisayarda yüklü olanlar listelenir, **hiçbiri varsayılan seçili gelmez**.
  Kaldırılanlar istenirse Store'dan geri yüklenebilir.
- **Program kur:** yeni bilgisayar kurulumu. Sık kullanılan ~45 program (tarayıcılar, Discord, Steam, VLC, 7-Zip…)
  kategorilere ayrılmış; işaretlenenler winget ile resmi kaynaklarından sırayla, sessizce kurulur. Zaten kurulu
  olanlar işaretlenemez. Liste `Core/AppCatalog.cs` içinde.
- **Format öncesi / sonrası program listesi:** **Listeyi kaydet…** yüklü programları (`winget list`) bir metin dosyasına
  yazar: winget'te olanlar kimlikleriyle, olmayanların sadece adı ve sürümü. winget'in eşleştiremediği ama hazır
  katalogda olan programlar adından eşleştirilir; başka programların kurduğu bağımlılıklar (VC++ 2005, .NET SDK,
  WinAppRuntime…) yazılmaz. Format sonrası **Program kur › Listeden yükle…** dosyayı açar: winget'te olanlar işaretli
  gelir (zaten kurulu olanlar hariç), **Seçilenleri kur** ile tek seferde kurulur; olmayanlar "Web'de ara" butonuyla
  listede durur. `winget export` ile alınmış JSON dosyası da açılabilir.

## Araçlar

- **Bu bilgisayar:** model, Windows sürümü, **Windows kurulum tarihi** (büyük sürüm yükseltmesiyle sıfırlandıysa ilk kurulum
  tarihi de), son açılış, işlemci, RAM, ekran kartları ve sürücü sürümleri, diskler, anakart, BIOS. "Kopyala" ile panoya alınır.
- **Ekran kartı sürücüsü:** NVIDIA kartlarda NVIDIA'nın sürücü servisinden en yeni Game Ready sürücü sorgulanır;
  yeni sürüm varsa program açılışında üstte turuncu bir şerit çıkar (Gizle dersen o sürüm için bir daha çıkmaz) ve
  otomatik bakımın bildirimine eklenir. Sürücüyü kendisi kurmaz, sürücü sayfasını açar.
  AMD ve Intel böyle bir servis sunmadığı için onlarda kurulu sürüm, tarihi ve üreticinin sayfası gösterilir.
- **Disk sağlığı:** SMART durumu, SSD aşınması ve sıcaklık.
- **Pil sağlığı** (dizüstünde): şu anki tam şarj kapasitesinin tasarım kapasitesine oranı; "Ayrıntılı rapor" Windows'un
  pil raporunu (`powercfg /batteryreport`) açar ("Rapor").
- **Sistem onarımı:** SFC, DISM RestoreHealth, CHKDSK /scan, sürücü optimizasyonu (SSD'ye TRIM, HDD'ye birleştirme),
  **Windows Update'i sıfırla** (servisleri durdurup SoftwareDistribution ve catroot2'yi `.bak` olarak kenara alır).
- **Ağ araçları:** DNS önbelleğini temizle, IP adresini yenile, ağ ayarlarını sıfırla (Winsock / TCP-IP; yeniden başlatma ister).
- **Hazırda bekletme:** hiberfil.sys'i kapat / azaltılmış / tam.
- **Eski sürücü paketleri:** DriverStore'da daha yeni sürümü kurulu olan eski paketler. `pnputil /delete-driver` ile
  (`/force` olmadan) silinir; kullanımdaki bir paketi Windows zaten silmez.
- **Windows Update:** son yüklenen güncelleme, bekleyen güncellemeler, yeniden başlatma bekleniyor mu; **1 hafta ertele**
  (Ayarlar'daki "Güncellemeleri duraklat" ile aynı kayıt değerleri, yönetici ister).
- **Güç ve performans:** güç planı seçimi, gizli "Nihai performans" planını ekleme, oyun modu, saydamlık efektleri, animasyonlar.
- **Geri yükleme noktaları:** listeler, seçilenleri siler (`SRRemoveRestorePoint`), toplam kapladığı yeri gösterir ve
  alan sınırını %3 / %5 / %10 yapar (`vssadmin resize shadowstorage`). "Yeni nokta oluştur" Sistem Koruması kapalıysa açmayı teklif eder.

## Tek tıkla bakım ve otomatik bakım

Sol alttaki **Tek tıkla bakım** butonu bir pencere açar; neyin yapılacağını oradan seçersin:

- **Temizlik:** hangi kategorilerin temizleneceği (Temizlik sayfasındaki seçimden bağımsız; "Önerilenler" /
  "Temizlik sekmesindekiler" kısayolları var).
- **Uygulama güncellemeleri** (Güncellemeler sayfasında "Atla" dediklerin hariç), **eski sürücü paketleri**,
  **sürücü optimizasyonu** (TRIM). Bunlar varsayılan kapalıdır.
- **Güncellemeden önce geri yükleme noktası**, **bitince bilgisayarı kapat** (sadece o sefer için; 60 sn içinde
  `shutdown /a` ile iptal edilebilir).
- **Otomatik çalıştır:** kapalı / her gün / her hafta / 2 haftada bir; gün ve saat seçilir. Görev Zamanlayıcı'ya kaydedilir;
  bilgisayar o saatte kapalıysa ilk açılışta yapılır, bitince Windows bildirimi gösterilir (güncellenemeyen uygulamalar da yazar).

**Kaydet** başlatmadan kaydeder (otomatik bakımı ayarlamak için), **Başlat** kaydedip hemen çalıştırır.

- Elle de çalıştırılabilir: `Kahya.exe /auto`. Sonuç `Data\last-run.log` dosyasına yazılır.
- Her temizlik/bakım `Data\history.csv` dosyasına kaydedilir; Temizlik sayfasında aylık özet,
  Hakkında sayfasında son 15 kayıt görünür.

## Gizlilik

Windows'un reklam/öneri/veri toplama ayarları için anahtarlar: reklam kimliği, Başlat önerileri, kilit ekranı reklamları,
ipucu bildirimleri, önerilen uygulamaların kendiliğinden yüklenmesi, Başlat'ta Bing sonuçları, Gezgin'deki OneDrive tanıtımları,
kişiye özel deneyimler, geri bildirim istekleri, telemetri seviyesi, etkinlik geçmişi.
Sadece kayıt defteri değerleri değişir (servis kapatılmaz). İlk değişiklikten önce her değerin o anki hali
`Data\privacy-backup.ini` dosyasına yedeklenir: anahtarı kapatmak o değeri geri yazar, **Değişiklikleri geri al** hepsini
Kahya'dan önceki haline döndürür.
**Önerilenleri uygula** sadece "önerilen" etiketli olanları açar; grup ilkesi kullanan son ikisi isteğe bağlıdır.

### Tarayıcı eklentileri

Gizlilik › Tarayıcı eklentileri: Chrome, Edge, Brave, Vivaldi ve Opera'daki eklentiler (tüm profiller). Sadece okunur.
Mağaza dışından kurulmuş, 1 yıldan uzun süredir güncellenmemiş ya da hata ayıklayıcı / proxy / eklenti yönetimi izni
isteyenler turuncu işaretlenir; bütün sitelere erişim gibi izinler bilgi olarak yazılır.

## Durum raporu

Genel bakış › Rapor: kutucuklar, sistem bilgisi, diskler, Windows Update, bekleyen güncellemeler, açılışta başlayanlar ve
son bakımlar tek sayfalık bir HTML dosyasına yazılır (açık/koyu temaya uyar, tarayıcıdan PDF olarak kaydedilebilir).

## Kopya dosya bulucu (Disk analizi)

Seçilen klasörde içeriği birebir aynı olan dosyaları (1 MB ve üstü) bulur: boyut → ilk 64 KB → tam SHA-1 karşılaştırması.
Sabit bağlantılar (aynı fiziksel dosya) kopya sayılmaz; OneDrive/Google Drive'da indirilmemiş dosyalar okunmaz.
Silinecekleri sen seçersin (her gruptan en az biri kalmak zorunda); silinenler Geri Dönüşüm Kutusu'na gider.
Sistem, program ve oyun klasörlerine girilmez (yukarıdaki "Hiç taranmayan yerler").

## Tema

Sistem (Windows'un uygulama temasına uyar, Windows'ta değişince anında değişir), Koyu ya da Açık. Hakkında › Ayarlar'dan
seçilir; sol üstteki güneş/ay düğmesi koyu ile açık arasında hızlıca geçer. Renkler `Core/Theme.cs` içinde iki palet olarak
durur ve XAML'de `DynamicResource` ile kullanılır, bu yüzden geçiş yeniden başlatma istemez.

## Dil

Türkçe ve İngilizce. Hakkında sayfasından değiştirilir (yeniden başlatınca uygulanır). İlk açılışta Windows dili
Türkçe değilse İngilizce gelir. Çeviriler `Core/LangEn.cs` içinde; kod ve XAML'de metinler Türkçe yazılır.

## Yeni sürüm kontrolü ve kendini güncelleme

Kapalı gelir. Açmak için GitHub'da bir depo oluştur, sürümleri `v1.1.0` gibi etiketlerle "Release" olarak yayınla,
sürüme `Kahya.exe`'yi (ya da onu içeren bir .zip'i) ekle ve `Core/UpdateChecker.cs` içindeki `Repo` sabitine
`kullaniciadi/Kahya` yaz. Uygulama açılışta kontrol eder; yeni sürüm varsa üstte bir şerit çıkar. **Güncelle**
dosyayı indirir, gerçekten daha yeni bir Kahya olduğunu doğrular, çalışan exe'yi `Kahya.exe.old` yapıp yenisini
yerine koyar ve programı yeniden açar (eski dosya sonraki açılışta silinir).

## Disk dolunca uyarı

Otomatik bakım bittiğinde sistem diskinde %10'dan az yer kaldıysa bildirime uyarı eklenir.
## Yeni klasör eklemek

`Core/Catalog.cs` içine bir öğe eklemen yeterli:

```csharp
items.Add(Make("ornek", GApps, "Örnek uygulama önbelleği", "Açıklama", true,
    R(@"%LOCALAPPDATA%\Ornek\Cache"),
    R(@"%APPDATA%\Ornek\*\GPUCache")));   // '*' klasör adında joker olarak kullanılabilir
```
