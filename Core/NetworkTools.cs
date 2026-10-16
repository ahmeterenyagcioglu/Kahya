using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Hazır bir DNS sunucusu seçeneği. Sunucular boşsa "otomatik" (ağın / modemin verdiği).</summary>
    public sealed class DnsPreset
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string[] V4 { get; set; } = new string[0];
        public string[] V6 { get; set; } = new string[0];
        public bool Automatic => V4.Length == 0;
    }

    /// <summary>Bağlı ağ kartlarının DNS ayarı ve hosts dosyası.</summary>
    public static partial class NetworkTools
    {
        public static List<DnsPreset> Presets => new List<DnsPreset>
        {
            new DnsPreset { Id = "auto", Name = T("Otomatik"), Description = T("Modemin ya da internet sağlayıcının verdiği DNS (Windows varsayılanı).") },
            new DnsPreset { Id = "cloudflare", Name = "Cloudflare", Description = T("Hızlı ve gizlilik odaklı; sorguları kaydetmediğini söylüyor."),
                            V4 = new[] { "1.1.1.1", "1.0.0.1" }, V6 = new[] { "2606:4700:4700::1111", "2606:4700:4700::1001" } },
            new DnsPreset { Id = "google", Name = "Google", Description = T("Hızlı ve her yerde çalışır."),
                            V4 = new[] { "8.8.8.8", "8.8.4.4" }, V6 = new[] { "2001:4860:4860::8888", "2001:4860:4860::8844" } },
            new DnsPreset { Id = "quad9", Name = "Quad9", Description = T("Bilinen zararlı siteleri engeller."),
                            V4 = new[] { "9.9.9.9", "149.112.112.112" }, V6 = new[] { "2620:fe::fe", "2620:fe::9" } },
            new DnsPreset { Id = "adguard", Name = "AdGuard", Description = T("Reklam ve izleyici sitelerini engeller; bazı siteler bozuk görünebilir."),
                            V4 = new[] { "94.140.14.14", "94.140.15.15" }, V6 = new[] { "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff" } },
        };

        /// <summary>Bağlı (Up) fiziksel ağ kartları: adı ve elle girilmiş IPv4 DNS sunucuları (boşsa otomatik).</summary>
        public static List<(string adapter, List<string> servers)> CurrentDns(CleanContext ctx)
        {
            // Elle girilmiş DNS kayıt defterindeki NameServer'dadır; boşsa DHCP'den (otomatik) gelir.
            var lines = PowerShellRunner.Run(@"
Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object Status -eq 'Up' | ForEach-Object {
    $ns = (Get-ItemProperty ('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\' + $_.InterfaceGuid) -ErrorAction SilentlyContinue).NameServer
    $_.Name + [char]9 + $ns
}", ctx);
            var list = new List<(string, List<string>)>();
            foreach (var line in lines)
            {
                var p = line.Split('\t');
                if (p.Length < 2 || p[0].Length == 0) continue;
                // Değerin sonunda boşluk ya da NUL dolgusu olabiliyor; yalnızca adres karakterleri alınır.
                var servers = System.Text.RegularExpressions.Regex.Matches(p[1], @"[0-9A-Fa-f:.]+").Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Value).Where(v => v.Contains('.') || v.Contains(':')).ToList();
                list.Add((p[0], servers));
            }
            return list;
        }

        /// <summary>Kartlardaki DNS'in hangi hazır seçenek olduğu; karışıksa ya da listede yoksa null.</summary>
        public static DnsPreset Match(List<(string adapter, List<string> servers)> current)
        {
            if (current.Count == 0) return null;
            DnsPreset match = null;
            foreach (var (_, servers) in current)
            {
                var m = Presets.FirstOrDefault(p => p.V4.Length == servers.Count && p.V4.All(s => servers.Contains(s)));
                if (m == null || (match != null && match.Id != m.Id)) return null;
                match = m;
            }
            return match;
        }

        /// <summary>Bağlı fiziksel kartların hepsine uygular ve DNS önbelleğini temizler. Yönetici ister.</summary>
        public static void SetDns(DnsPreset preset, CleanContext ctx)
        {
            var set = preset.Automatic
                ? "Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses -ErrorAction Stop"
                : "Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses @(" +
                  string.Join(",", preset.V4.Concat(preset.V6).Select(PowerShellRunner.Quote)) + ") -ErrorAction Stop";
            var lines = PowerShellRunner.Run(@"
$n = 0
Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object Status -eq 'Up' | ForEach-Object {
    try { " + set + @"; $n++ } catch { 'E' + [char]9 + $_.Name + ': ' + $_.Exception.Message }
}
Clear-DnsClientCache -ErrorAction SilentlyContinue
'N' + [char]9 + $n", ctx);
            var errors = lines.Where(l => l.StartsWith("E\t")).Select(l => l.Substring(2)).ToList();
            var count = lines.Where(l => l.StartsWith("N\t")).Select(l => int.TryParse(l.Substring(2), out var n) ? n : 0).FirstOrDefault();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            if (count == 0) throw new InvalidOperationException(T("Bağlı bir ağ kartı bulunamadı."));
        }

        // ───────────── hosts ─────────────

        public static string HostsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

        /// <summary>Windows'un kurulumla gelen hosts dosyası (yalnızca açıklamalar, etkin satır yok).</summary>
        public const string DefaultHosts =
@"# Copyright (c) 1993-2009 Microsoft Corp.
#
# This is a sample HOSTS file used by Microsoft TCP/IP for Windows.
#
# This file contains the mappings of IP addresses to host names. Each
# entry should be kept on an individual line. The IP address should
# be placed in the first column followed by the corresponding host name.
# The IP address and the host name should be separated by at least one
# space.
#
# Additionally, comments (such as these) may be inserted on individual
# lines or following the machine name denoted by a '#' symbol.
#
# For example:
#
#      102.54.94.97     rhino.acme.com          # source server
#       38.25.63.10     x.acme.com              # x client host

# localhost name resolution is handled within DNS itself.
#	127.0.0.1       localhost
#	::1             localhost
";

        public static string ReadHosts()
        {
            try { return File.ReadAllText(HostsPath); }
            catch (FileNotFoundException) { return ""; }
        }

        /// <summary>Açıklama olmayan, boş olmayan satırlar (gerçekten bir adı yönlendirenler).</summary>
        public static int ActiveEntries(string text) =>
            (text ?? "").Split('\n').Select(l => l.Trim()).Count(l => l.Length > 0 && !l.StartsWith("#"));

        /// <summary>
        /// Önce o anki hali Data\hosts-yedek-tarih.txt olarak yedekler, sonra yazar ve DNS önbelleğini temizler.
        /// Yedeğin yolunu döndürür. Yönetici ister. Kodlama BOM'suz (hosts'u okuyan Windows bileşeni BOM'u tanımaz).
        /// </summary>
        public static string SaveHosts(string text, CleanContext ctx)
        {
            Directory.CreateDirectory(Settings.Folder);
            var backup = Path.Combine(Settings.Folder, $"hosts-yedek-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            if (File.Exists(HostsPath)) File.Copy(HostsPath, backup, true);
            var attributes = File.Exists(HostsPath) ? File.GetAttributes(HostsPath) : FileAttributes.Normal;
            // Bazı "reklam engelleyici" araçlar hosts'u salt okunur yapar; yazmak için kaldırılır, sonra geri konur.
            if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(HostsPath, attributes & ~FileAttributes.ReadOnly);
            var normalized = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            File.WriteAllText(HostsPath, normalized, new UTF8Encoding(false));
            if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(HostsPath, attributes);
            ProcessRunner.Run("ipconfig.exe", "/flushdns", ctx);
            return backup;
        }
    }
}
