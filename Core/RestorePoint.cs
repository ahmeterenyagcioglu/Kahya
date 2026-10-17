using System.Linq;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Sistem Geri Yükleme noktası oluşturur (Windows PowerShell'in Checkpoint-Computer komutuyla).</summary>
    public static class RestorePoint
    {
        /// <summary>Başarılıysa null, değilse hata mesajı döndürür.</summary>
        public static string Create(string description, CleanContext ctx)
        {
            // Windows varsayılan olarak 24 saatte en fazla bir nokta oluşturur ve fazlasını sessizce yok sayar.
            // Bu sınırı sadece bu işlem süresince kaldırıp eski değerini geri yazıyoruz.
            var script = @"
$k = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
$old = (Get-ItemProperty $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue).SystemRestorePointCreationFrequency
Set-ItemProperty $k -Name SystemRestorePointCreationFrequency -Value 0 -Type DWord
try {
    Checkpoint-Computer -Description " + PowerShellRunner.Quote(description) + @" -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
    'WINCLEANER_OK'
} catch {
    'WINCLEANER_ERR:' + $_.Exception.Message
} finally {
    if ($null -eq $old) { Remove-ItemProperty $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue }
    else { Set-ItemProperty $k -Name SystemRestorePointCreationFrequency -Value $old }
}";
            return Result(PowerShellRunner.Run(script, ctx).ToArray());
        }

        /// <summary>
        /// Sistem Korumasını açar; geri yükleme noktasının dayandığı Gölge Kopya hizmetleri (VSS, swprv) "devre dışı"
        /// yapılmışsa onları da "el ile" başlatılır hale getirir (Windows varsayılanı).
        /// </summary>
        public static string EnableOnSystemDrive(CleanContext ctx) =>
            Result(PowerShellRunner.Run(
                "try { foreach ($s in 'VSS','swprv') { if ((Get-Service $s -ErrorAction SilentlyContinue).StartType -eq 'Disabled') { Set-Service $s -StartupType Manual -ErrorAction Stop } } " +
                "Enable-ComputerRestore -Drive \"$env:SystemDrive\\\" -ErrorAction Stop; 'WINCLEANER_OK' } " +
                "catch { 'WINCLEANER_ERR:' + $_.Exception.Message }", ctx).ToArray());

        static string Result(string[] lines)
        {
            if (lines.Any(l => l.Contains("WINCLEANER_OK")))
                return null;
            var err = lines.FirstOrDefault(l => l.StartsWith("WINCLEANER_ERR:"));
            return err != null ? Friendly(err.Substring(15).Trim()) : T("bilinmeyen hata");
        }

        /// <summary>PowerShell'in İngilizce (ya da Windows diline göre) hata metni yerine ne olduğunu söyler.</summary>
        internal static string Friendly(string message)
        {
            var m = message.ToLowerInvariant();
            if (m.Contains("disabled or does not have enabled devices") || m.Contains("devre dışı bırakıldığından") || m.Contains("0x80070422"))
                return T("Gölge Kopya (Volume Shadow Copy) hizmeti kapatılmış.");
            if (m.Contains("turned off") || m.Contains("system restore is disabled") || m.Contains("0x81000101") || m.Contains("kapalı"))
                return T("Sistem sürücüsünde Sistem Koruması kapalı.");
            if (m.Contains("access") && m.Contains("denied"))
                return T("Yönetici izni gerekiyor.");
            if (m.Contains("in progress") || m.Contains("0x81000102"))
                return T("Şu an başka bir geri yükleme noktası oluşturuluyor; birazdan tekrar dene.");
            return message;
        }
    }
}
