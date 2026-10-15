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

        public static string EnableOnSystemDrive(CleanContext ctx) =>
            Result(PowerShellRunner.Run(
                "try { Enable-ComputerRestore -Drive \"$env:SystemDrive\\\" -ErrorAction Stop; 'WINCLEANER_OK' } " +
                "catch { 'WINCLEANER_ERR:' + $_.Exception.Message }", ctx).ToArray());

        static string Result(string[] lines)
        {
            if (lines.Any(l => l.Contains("WINCLEANER_OK")))
                return null;
            var err = lines.FirstOrDefault(l => l.StartsWith("WINCLEANER_ERR:"));
            return err != null ? err.Substring(15).Trim() : T("bilinmeyen hata");
        }
    }
}
