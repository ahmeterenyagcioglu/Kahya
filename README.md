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
| **.NET Framework 4.8** | Windows 10 1903 ve sonrasında, Windows 11'de **zaten yüklüdür**. Daha eski bir Windows 10'da açılmazsa [buradan](https://dotnet.microsoft.com/download/dotnet-framework/net48) kurulur. |
| **İnternet** | Sadece uygulama güncellemeleri, program kurma ve yeni sürüm kontrolü için. Temizlik ve diğer her şey çevrimdışı çalışır. |

İsteğe bağlı:

- **winget (Uygulama Yükleyici):** Güncellemeler ve Program kur sayfaları için. Windows 10/11'de genelde yüklü gelir;
  yoksa Microsoft Store'dan "Uygulama Yükleyici" (App Installer) kurulur.
- **7-Zip:** Format yedeğindeki sürücüleri `.7z` olarak sıkıştırmak için. Yoksa `.zip` kullanılır.
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
- Sürücüler (klasör, ZIP ya da 7z)
- Masaüstü, Belgeler, İndirilenler gibi kişisel klasörler
- Tarayıcı yer imleri, sonradan kurulan fontlar
- Program ayarları (Premiere, Photoshop, Lightroom, CapCut, OBS…)

Format sonrası aynı sayfadan hepsi geri yüklenir.

### 📊 Disk analizi
En çok yer kaplayan klasörler, büyük ve uzun süredir dokunulmamış dosyalar, dosya türlerine göre dağılım,
**kopya dosyalar**, boş klasörler ve video programlarının bıraktığı render artıkları.

### 🚀 Başlangıç
Windows açılınca başlayan uygulamalar, zamanlanmış görevler ve servisler; tek anahtarla açılıp kapatılır, hiçbir şey silinmez.

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
- **Taşınabilir:** tüm ayarlar exe'nin yanındaki `Data` klasöründe durur. Klasörü silmek programı tamamen kaldırır.

Kaynak kodun tamamı bu depoda.

## Sorun bildirme

Bir hata görürsen [Issues](https://github.com/ahmeterenyagcioglu/Kahya/issues) sayfasından bildirebilirsin.
Kahya beklenmeyen hataları `Data\errors.log` dosyasına yazar; o dosyayı eklemek sorunu bulmayı çok kolaylaştırır.

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
  `Core/AppInfo.cs` içindeki `ReleaseNotes`'a not ekle, `vX.Y.Z` etiketiyle GitHub Release oluşturup `Kahya.exe`'yi ekle.
  Kullanıcılardaki Kahya yeni sürümü buradan bulur.

---

© 2026 Ahmet Eren Yağcıoğlu
