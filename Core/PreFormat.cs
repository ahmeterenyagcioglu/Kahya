using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Bir diskin BitLocker durumu ve (yöneticiyse) kurtarma anahtarı.</summary>
    public sealed class BitLockerVolume
    {
        public string Drive;
        /// <summary>Şifreli (ya da şifreleniyor).</summary>
        public bool Encrypted;
        /// <summary>48 haneli kurtarma parolaları ve kimlikleri; yönetici değilse okunamaz (boş).</summary>
        public List<(string id, string password)> Keys = new List<(string, string)>();
    }

    /// <summary>Formattan önce bakılması gerekenlerden biri (kontrol listesi satırı).</summary>
    public sealed class PreFormatCheck
    {
        public string Icon { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        /// <summary>Kahya kendisi hallediyor (ör. BitLocker anahtarı yedeğe giriyor): bilgi amaçlı, işaretli gelir.</summary>
        public bool Handled;
        /// <summary>Kullanıcı işaretledi (kontrol listesinde; Kahya'nın hallettikleri işaretli gelir).</summary>
        public bool Done { get; set; }
        /// <summary>Satırın altındaki düğmeler (ör. her tarayıcının şifre sayfasını açan).</summary>
        public List<CheckAction> Actions { get; } = new List<CheckAction>();
        public bool HasActions => Actions.Count > 0;
    }

    /// <summary>Kontrol listesindeki bir düğme: bir programı verilen adresle açar.</summary>
    public sealed class CheckAction
    {
        public string Label { get; set; }
        public string Exe { get; set; }
        public string Args { get; set; }
    }

    /// <summary>Kurulu bir tarayıcı ve şifrelerini dışa aktarabileceğin sayfası.</summary>
    public sealed class BrowserInfo
    {
        public string Name;
        public string Exe;
        /// <summary>Tarayıcının kendi iç adresi; dışarıdan ancak komut satırından açılabilir (bağlantı olarak açılmaz).</summary>
        public string PasswordPage;
    }

    /// <summary>
    /// Formattan önce: BitLocker kurtarma anahtarları ve yedeğe giremeyen ama formatta kaybolabilecek şeylerin hatırlatması
    /// (tarayıcı şifreleri, OneDrive, iki adımlı doğrulama, lisanslar, oyun hesapları).
    /// </summary>
    public static class PreFormat
    {
        // ───────────── BitLocker ─────────────

        /// <summary>
        /// Sabit disklerin BitLocker durumu. Şifreli olup olmadığı yönetici olmadan da (Gezgin'in gösterdiği özellikten)
        /// okunur; kurtarma anahtarı için manage-bde, o da yönetici ister.
        /// </summary>
        public static List<BitLockerVolume> BitLocker(CleanContext ctx)
        {
            var list = new List<BitLockerVolume>();
            foreach (var d in SafeDrives())
            {
                var v = new BitLockerVolume { Drive = d.TrimEnd('\\') };
                v.Encrypted = IsEncrypted(d);
                if (v.Encrypted && Elevation.IsAdmin)
                {
                    var lines = new List<string>();
                    var capture = new CleanContext(l => { lock (lines) lines.Add(l); }, ctx.Token);
                    ProcessRunner.Run("manage-bde.exe", $"-protectors -get {v.Drive} -Type RecoveryPassword", capture, logOutput: true);
                    string id = null;
                    foreach (var line in lines)
                    {
                        var m = Regex.Match(line, @"\{[0-9A-Fa-f\-]{36}\}");
                        if (m.Success) id = m.Value;
                        var p = Regex.Match(line, @"\b\d{6}(-\d{6}){7}\b");
                        if (p.Success) v.Keys.Add((id ?? "", p.Value));
                    }
                }
                if (v.Encrypted) list.Add(v);
            }
            return list;
        }

        static IEnumerable<string> SafeDrives()
        {
            try { return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).Select(d => d.Name).ToList(); }
            catch { return new List<string>(); }
        }

        /// <summary>
        /// Gezgin'in "System.Volume.BitLockerProtection" özelliği: 1 açık, 3 şifreleniyor, 5 kilitli… (2 kapalı, 0 yok).
        /// Shell COM nesnesiyle okunur, yönetici gerekmez.
        /// </summary>
        static bool IsEncrypted(string drive)
        {
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                var shell = Activator.CreateInstance(type);
                var computer = type.InvokeMember("NameSpace", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { 17 }); // Bu bilgisayar
                var item = computer.GetType().InvokeMember("ParseName", System.Reflection.BindingFlags.InvokeMethod, null, computer, new object[] { drive });
                if (item == null) return false;
                var value = item.GetType().InvokeMember("ExtendedProperty", System.Reflection.BindingFlags.InvokeMethod, null, item, new object[] { "System.Volume.BitLockerProtection" });
                int state = value == null ? 0 : Convert.ToInt32(value);
                return state == 1 || state == 3 || state == 4 || state == 5 || state == 6;
            }
            catch { return false; }
        }

        /// <summary>Kurtarma anahtarlarını yedeğe yazar (düz metin; dosyanın yerinde durması kullanıcıya hatırlatılır).</summary>
        public static void SaveBitLocker(List<BitLockerVolume> volumes, string file)
        {
            var sb = new StringBuilder();
            sb.AppendLine(T("BitLocker kurtarma anahtarları — {0} · {1:dd.MM.yyyy HH:mm}", Environment.MachineName, DateTime.Now));
            sb.AppendLine(T("Format sonrası şifreli bir disk açılmazsa bu 48 haneli anahtar sorulur. Bu dosyayı kimseyle paylaşma."));
            sb.AppendLine();
            foreach (var v in volumes)
            {
                sb.AppendLine(v.Drive);
                if (v.Keys.Count == 0) sb.AppendLine("   " + T("(anahtar okunamadı; Kahya'yı yönetici olarak açıp tekrar yedekle ya da account.microsoft.com/devices/recoverykey)"));
                foreach (var (id, password) in v.Keys)
                    sb.AppendLine($"   {T("Kimlik")}: {id}\r\n   {T("Kurtarma anahtarı")}: {password}");
                sb.AppendLine();
            }
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
        }

        // ───────────── kontrol listesi ─────────────

        /// <summary>Bu bilgisayara göre formattan önce bakılacaklar (kurulu olmayan şeyler için satır çıkmaz).</summary>
        public static List<PreFormatCheck> Checklist(List<BitLockerVolume> bitlocker)
        {
            var list = new List<PreFormatCheck>();
            void Add(string icon, string title, string detail, bool handled = false) =>
                list.Add(new PreFormatCheck { Icon = icon, Title = title, Detail = detail, Handled = handled, Done = handled });

            if (bitlocker.Count > 0)
            {
                var drives = string.Join(", ", bitlocker.Select(v => v.Drive));
                bool keys = bitlocker.All(v => v.Keys.Count > 0);
                Add("\uE72E", T("BitLocker ile şifreli diskler: {0}", drives),
                    keys ? T("Kurtarma anahtarları yedeğe eklenecek.")
                         : T("Anahtarı okumak için Kahya'yı yönetici olarak aç."),
                    keys);
            }

            var browsers = InstalledBrowsers();
            if (browsers.Count > 0)
            {
                Add("\uE8D7", T("Tarayıcı şifreleri ({0})", string.Join(", ", browsers.Select(b => b.Name))),
                    T("Yedeğe girmez. Eşitlenmiyorsa dışa aktar; dosyayı içe aktarınca sil."));
                foreach (var b in browsers.Where(b => b.Exe != null))
                    list.Last().Actions.Add(new CheckAction { Label = T("{0} şifreleri", b.Name), Exe = b.Exe, Args = b.PasswordPage });
            }

            if (Exists(@"%LOCALAPPDATA%\Microsoft\OneDrive\OneDrive.exe") || Process.GetProcessesByName("OneDrive").Length > 0)
                Add("\uE753", T("OneDrive"), T("Simgede yeşil tik olana kadar bekle."));

            Add("\uE8D4", T("İki adımlı doğrulama"),
                T("Kodlar bilgisayardaki bir uygulamadaysa önce telefona taşı."));

            if (Exists(@"%ProgramFiles%\Microsoft Office\root") || Exists(@"%ProgramFiles(x86)%\Microsoft Office"))
                Add("\uE8A5", T("Microsoft Office"), T("Microsoft hesabına bağlı değilse ürün anahtarını bul."));
            if (Exists(@"%ProgramFiles%\Adobe") || Exists(@"%CommonProgramFiles%\Adobe\OOBE"))
                Add("\uE8A5", T("Adobe"), T("Yardım › Oturumu kapat; etkinleştirme sınırına takılmazsın."));

            var launchers = new List<string>();
            if (Exists(@"%ProgramFiles(x86)%\Steam")) launchers.Add("Steam");
            if (Exists(@"%LOCALAPPDATA%\Discord")) launchers.Add("Discord");
            if (Exists(@"%ProgramFiles(x86)%\Epic Games") || Exists(@"%ProgramFiles%\Epic Games")) launchers.Add("Epic Games");
            if (Exists(@"C:\Riot Games")) launchers.Add("Riot");
            if (Exists(@"%ProgramFiles(x86)%\Battle.net")) launchers.Add("Battle.net");
            if (Exists(@"%ProgramFiles%\EA Games") || Exists(@"%ProgramFiles%\Electronic Arts")) launchers.Add("EA");
            if (launchers.Count > 0)
                Add("\uE7FC", T("Oyun ve sohbet hesapları ({0})", string.Join(", ", launchers)),
                    T("Format sonrası yeniden gireceksin; şifrelerini bildiğinden emin ol."));

            Add("\uE88E", T("Format USB'si"), T("Yedeğin olduğu USB'yi kullanma; en az 8 GB ayrı bir USB."));
            return list;
        }

        static bool Exists(string path)
        {
            var p = Environment.ExpandEnvironmentVariables(path);
            return Directory.Exists(p) || File.Exists(p);
        }

        /// <summary>Kullanıcı verisi olan tarayıcılar (yalnızca kurulu olup hiç açılmamışlar sayılmaz).</summary>
        static List<BrowserInfo> InstalledBrowsers()
        {
            var list = new List<BrowserInfo>();
            void Add(string name, string data, string exe, string page)
            {
                if (Exists(data)) list.Add(new BrowserInfo { Name = name, Exe = AppPath(exe), PasswordPage = page });
            }
            Add("Chrome", @"%LOCALAPPDATA%\Google\Chrome\User Data", "chrome.exe", "chrome://password-manager/settings");
            Add("Edge", @"%LOCALAPPDATA%\Microsoft\Edge\User Data", "msedge.exe", "edge://wallet/passwords");
            Add("Firefox", @"%APPDATA%\Mozilla\Firefox\Profiles", "firefox.exe", "about:logins");
            Add("Brave", @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data", "brave.exe", "brave://password-manager/settings");
            Add("Opera", @"%APPDATA%\Opera Software", "opera.exe", "opera://settings/passwords");
            return list;
        }

        /// <summary>Programın tam yolu, Windows'un "App Paths" kaydından (Çalıştır penceresi de buradan bulur). Yoksa null.</summary>
        static string AppPath(string exe)
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                    using (var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                    {
                        var path = (k?.GetValue("") as string)?.Trim().Trim('"');
                        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                    }
                }
                catch { }
            }
            // Opera App Paths'e yazmaz; kullanıcı klasörüne kurulur.
            if (exe == "opera.exe")
            {
                var opera = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Programs\Opera\opera.exe");
                if (File.Exists(opera)) return opera;
            }
            return null;
        }

        // ───────────── format USB'si ─────────────

        /// <summary>Microsoft'un Medya Oluşturma Aracı (Windows 11 / 10): format USB'sini hazırlar.</summary>
        public static string MediaCreationToolUrl(bool windows11) =>
            windows11 ? "https://go.microsoft.com/fwlink/?linkid=2156295" : "https://go.microsoft.com/fwlink/?LinkId=691209";

        public static bool IsWindows11()
        {
            try
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    return int.TryParse(k?.GetValue("CurrentBuild") as string, out var b) && b >= 22000;
            }
            catch { return true; }
        }
    }
}
