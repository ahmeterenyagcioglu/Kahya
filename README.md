# Kahya

Windows için hepsi bir arada bakım aracı: gereksiz dosyaları temizler, uygulamaları günceller, açılışı hızlandırır,
diskte neyin yer kapladığını gösterir ve format atmadan önce her şeyi tek seferde yedekler.

Tek dosyadır, kurulum gerektirmez. Türkçe ve İngilizce.

**[⬇ Son sürümü indir](https://github.com/ahmeterenyagcioglu/Kahya/releases/latest)**

---

## Kurulum

1. [Sürümler](https://github.com/ahmeterenyagcioglu/Kahya/releases/latest) sayfasından `Kahya.exe`'yi indir.
2. İstediğin bir klasöre koy ve çalıştır. O kadar.

> **"Windows bilgisayarınızı korudu" uyarısı çıkarsa:** Kahya dijital olarak imzalı olmadığı için Windows SmartScreen
> ilk açılışta uyarabilir. **Ek bilgi › Yine de çalıştır** ile açılır. Bu uyarı bir kez çıkar.

Yeni sürüm çıktığında Kahya bunu kendisi fark eder ve üstte bir şerit gösterir; **Güncelle** ile tek tıkla güncellenir.

## Gereksinimler

| | |
|---|---|
| **İşletim sistemi** | Windows 10 (sürüm 1903 ya da sonrası) veya Windows 11 |
| **.NET Framework 4.8** | Windows 10 1903 ve sonrasında, Windows 11'de **zaten yüklüdür**. Daha eski bir sürüm varsa Kahya açılışta söyler ve [indirme sayfasını](https://dotnet.microsoft.com/download/dotnet-framework/net48) açar. |
| **İnternet** | Sadece uygulama güncellemeleri, program kurma ve yeni sürüm kontrolü için. Temizlik ve diğer her şey çevrimdışı çalışır. |

Gerektiğinde Kahya kendisi kurar (önce sorar):

- **winget (Uygulama Yükleyici):** Güncellemeler ve Program kur sayfaları için. Windows 10/11'de genelde yüklü gelir;
  yoksa Kahya tek tıkla kurar.
- **7-Zip:** Format yedeğindeki sürücüleri `.7z` olarak sıkıştırmak için. 7z seçilince yoksa kurulur; istemezsen `.zip` kullanılır.

Ayrıca:

- **Yönetici izni:** Kahya normal kullanıcı olarak açılır. Yönetici isteyen özellikler (Windows Temp, Prefetch,
  SFC/DISM, sürücüler, geri yükleme noktaları…) kalkan simgesiyle işaretlidir; tıklayınca Kahya yönetici olarak
  yeniden başlar ve aynı sayfaya döner.

## Neler yapar?

### 🧹 Temizlik
Temp klasörleri, Prefetch, Windows Update önbelleği, tarayıcı önbellekleri (Chrome, Edge, Brave, Opera, Firefox…),
Discord / Spotify / Steam, Premiere / Lightroom / CapCut önbellekleri, shader önbellekleri ve 30'dan fazla kategori.
Önce tarar, her kategoride ne kadar yer açılacağını gösterir; sen seçersin. Silmeden önce **önizleme** ile hangi
dosyaların gideceğini görebilirsin.

### 🔄 Uygulama güncellemeleri
Güncellemesi olan programları listeler, seçtiklerini sessizce günceller. Başarısız olan güncellemenin nedenini anlaşılır
bir dille yazar. İstemediğin uygulamayı **Atla** ile listeden çıkarırsın. İstersen öncesinde geri yükleme noktası alır.

### ⚡ Tek tıkla bakım
Temizlik, güncellemeler ve eski sürücü paketleri tek seferde. Her gün, her hafta ya da iki haftada bir
**otomatik** çalışacak şekilde ayarlanabilir; bitince bildirim gösterir.

### 💾 Format yardımcısı
Format atmadan önce tek ekrandan yedek:
- Yüklü programların listesi (format sonrası winget'te olanlar tek seferde, en yeni sürümüyle kurulur)
- Wi-Fi ağları ve şifreleri
- Windows ürün anahtarı
- Sürücüler
- Masaüstü, Belgeler, İndirilenler gibi kişisel klasörler
- Tarayıcı yer imleri, sonradan kurulan fontlar
- Program ayarları (Premiere, Photoshop, Lightroom, CapCut, OBS…)
- 🎮 **Oyun kayıtları:** Minecraft dünyaları, modları, kaynak ve gölgelendirici paketleri (CurseForge, Prism,
  Modrinth ve Bedrock dahil), Steam kayıtları, Unreal Engine (Epic) oyunları, Ubisoft Connect, Rockstar, Kayıtlı Oyunlar,
  My Games ve Unity oyunları. Oyunun kendisi yedeklenmez, sadece senin kayıtların. Bilinen yerlere bakılır; kaydını
  bambaşka bir yerde tutan oyunlar kaçabilir (Steam Cloud'u olanlar zaten güvende).

Hiçbir şey sorulmaz. Sonuç bir klasördür: içinde yedeğin tamamı tek bir ZIP (`Kahya-Yedek-BILGISAYARADI-tarih.zip`) ve
yanında `Kahya.exe`. ZIP'i Windows ek program olmadan açar; yedeğin ne içerdiği arşivin açıklamasına yazılır, Kahya yedeği
açmadan tanır. Wi-Fi ve Ethernet kartının sürücüleri sorulmadan her zaman yedeklenir: format sonrası internetsiz
kalırsan ilk onlar lazım. Belgeler gibi seçili bir klasörün içinde kalan oyun ve ayar klasörleri iki kez kopyalanmaz.

### 🧭 Format sonrası asistanı
Format sonrası yedek klasöründeki `Kahya.exe`'yi aç, **Format yardımcısı › Asistanı başlat** de. Kurulum sihirbazı gibi
adım adım ilerler:

1. **Yedek:** yanındaki ya da USB bellek / ikinci diskteki yedeği kendisi bulur.
2. **İnternet ve Wi-Fi:** kayıtlı Wi-Fi ağlarını şifreleriyle ekler (istediklerini seçersin); internet yoksa yedekteki ağ
   kartı sürücüsünü kurmayı teklif eder.
3. **Seçim:** her kategoriyi açıp içindekileri tek tek seçersin: sürücü paketleri, fontlar, kişisel klasörler, programlar,
   program ayarları, oyunlar. Zaten kurulu programlar kendiliğinden atlanır; Visual C++, .NET gibi bileşenler listelenmez.
4. **Ekran kartı:** Windows'un genel sürücüsü mü kullanılıyor, yeni sürücü var mı bakar; üreticinin sayfasını açar.
5. **Geri yükleme:** seçilenler sırayla. Var olan daha yeni dosyaların üzerine yazılmaz.

**Kaldığı yerden devam eder:** asistanı kapatırsan, Kahya'yı yeniden başlatırsan ya da bilgisayar kapanırsa hangi adımda
kaldığın, neleri seçtiğin ve neyin bittiği kaydedilir; bir sonraki açılışta asistan kendisi açılır ve kalanlarla sürer.
Sürücüler için yönetici izni gerekir; asistan tek tıkla yönetici olarak yeniden başlar ve kaldığı yerden açılır.
Eski sürümlerin klasör yedekleri (`.kahya`) de açılır.

### 📊 Disk analizi
En çok yer kaplayan klasörler, büyük ve uzun süredir dokunulmamış dosyalar, dosya türlerine göre dağılım,
**kopya dosyalar**, boş klasörler ve video programlarının bıraktığı render artıkları.

### 🚀 Başlangıç
Windows açılınca başlayan uygulamalar, zamanlanmış görevler ve servisler; tek anahtarla açılıp kapatılır, hiçbir şey silinmez.
Genel bakışta **açılış süresi** görünür: son açılış kaç saniye sürdü, 30 günlük ortalamaya göre yavaşladı mı ve açılışı
en çok hangi uygulama ya da servisler geciktiriyor. (Windows bu ölçümleri sadece yöneticiye gösterir; Kahya bir kez
yönetici olarak açılınca kaydedilir, sonra hep görünür.)

### 🛡 Gizlilik
Reklam kimliği, Başlat önerileri, kilit ekranı reklamları, Bing araması, telemetri gibi ayarlar tek yerde.
Her değişiklik yedeklenir; **Değişiklikleri geri al** ile hepsi eski haline döner. Tarayıcı eklentileri de denetlenir.

### 🔧 Araçlar
Sistem bilgisi, disk ve pil sağlığı, SFC / DISM / CHKDSK, ağ sıfırlama, ekran kartı sürücüsü kontrolü,
Windows Update durumu ve erteleme, güç planları, geri yükleme noktaları.

### Diğer
- Uygulamalar: program kaldırma, gereksiz Windows uygulamalarını kaldırma, sık kullanılan ~45 programı toplu kurma
- Bildirim alanında çalışma: disk dolunca ya da güncelleme çıkınca haber verir
- Koyu / açık tema, tek sayfalık bilgisayar durum raporu, ayarları dışa/içe aktarma

## Güvenli mi?

- Hiçbir şey sormadan silinmez. Hassas öğeler (çerezler, tarayıcı geçmişi) varsayılan olarak seçili gelmez.
- Tarayıcılarda sadece **önbellek** temizlenir; şifreler, geçmiş ve çerezler sen seçmedikçe kalır.
- Açık olan programların önbelleklerine dokunulmaz, kullanımdaki dosyalar atlanır.
- Kopya ve büyük dosyalar Geri Dönüşüm Kutusu'na gider.
- Sistem, program ve oyun klasörleri dosya aramalarında hiç taranmaz.
- **Taşınabilir:** tüm ayarlar exe'nin yanındaki `Data` klasöründe durur. Klasörü silmek programı kaldırır.
  Windows'a yazdığı tek şey `.kahya` dosyalarının çift tıklayınca Kahya ile açılması için bir dosya ilişkilendirmesidir
  (sadece senin kullanıcın için).

Kaynak kodun tamamı bu depoda.

## Sorun bildirme

Bir hata çıkarsa hata penceresinde **Evet** de ya da **Hakkında › Hata bildir**'e bas: sürüm, Windows ve hatanın
ayrıntısı hazır doldurulmuş bir [Issues](https://github.com/ahmeterenyagcioglu/Kahya/issues) sayfası açılır.
Aynı metin panoya da kopyalanır; GitHub hesabın yoksa WhatsApp vb. ile gönderebilirsin.
Hataların ayrıntıları `Data\errors.log` dosyasında da durur.

---

## Geliştirme

.NET Framework 4.8, WPF. Derlemek için .NET SDK yeterli:

```powershell
dotnet build -c Release
# çıktı: bin\Release\net48\Kahya.exe
```

- **Yeni temizlik kategorisi:** `Core/Catalog.cs` içine bir öğe ekle:
  ```csharp
  items.Add(Make("ornek", GApps, "Örnek uygulama önbelleği", "Açıklama", true,
      R(@"%LOCALAPPDATA%\Ornek\Cache"),
      R(@"%APPDATA%\Ornek\*\GPUCache")));   // '*' klasör adında joker olarak kullanılabilir
  ```
- **Çeviriler:** kod ve XAML'de metinler Türkçe yazılır, İngilizceleri `Core/LangEn.cs` içinde.
- **Tema:** renkler `Core/Theme.cs` içinde iki palet olarak durur.
- **Sürüm yayınlama:** `Kahya.csproj` içindeki `<Version>`'ı yükselt (düzeltme → `1.0.x`, yeni özellik → `1.x.0`),
  `Core/AppInfo.cs` içindeki `ReleaseNotes`'a not ekle ve commit'le. Sonra:
  ```powershell
  git tag -a v1.2.0 -m "• yeni şey"     # etiket mesajı sürüm notu olur
  git push --follow-tags
  ```
  GitHub Actions exe'yi derleyip Release olarak yayınlar (`.github/workflows/build.yml`); kullanıcılardaki Kahya yeni
  sürümü buradan bulur. Her push'ta da derleme kontrol edilir.

---

© 2026 Ahmet Eren Yağcıoğlu
