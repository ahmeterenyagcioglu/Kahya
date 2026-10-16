using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>İnterneti kesilmiş bir program (Kahya'nın güvenlik duvarı kuralı).</summary>
    public sealed class BlockedProgram
    {
        public string Path { get; set; }
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
        public bool Exists => File.Exists(Path);
        public string Details => Exists ? Path : Path + " · " + T("program artık yok");
    }

    /// <summary>
    /// Bir programın internete çıkışını Windows Güvenlik Duvarı kuralıyla keser. Kurallar "Kahya" grubunda durur:
    /// Kahya yalnız kendi kurallarını listeler ve siler, kullanıcının ya da başka programların kurallarına dokunmaz.
    /// Yönetici ister.
    /// </summary>
    public static class NetBlock
    {
        const string Group = "Kahya";

        public static List<BlockedProgram> List(CleanContext ctx)
        {
            var lines = PowerShellRunner.Run(
                "Get-NetFirewallRule -Group '" + Group + "' -ErrorAction SilentlyContinue | Get-NetFirewallApplicationFilter | ForEach-Object { $_.Program }", ctx);
            return lines.Select(l => l.Trim()).Where(l => l.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => new BlockedProgram { Path = Environment.ExpandEnvironmentVariables(p) })
                .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static void Block(string exe, CleanContext ctx)
        {
            if (!File.Exists(exe)) throw new FileNotFoundException(T("Program bulunamadı: {0}", exe));
            var name = PowerShellRunner.Quote(T("Kahya engeli: {0}", System.IO.Path.GetFileNameWithoutExtension(exe)));
            var program = PowerShellRunner.Quote(exe);
            Check(PowerShellRunner.Run(
                $"try {{ New-NetFirewallRule -DisplayName {name} -Group '{Group}' -Direction Outbound -Action Block -Program {program} -ErrorAction Stop | Out-Null; " +
                $"New-NetFirewallRule -DisplayName {name} -Group '{Group}' -Direction Inbound -Action Block -Program {program} -ErrorAction Stop | Out-Null }} " +
                "catch { 'E' + [char]9 + $_.Exception.Message }", ctx));
        }

        public static void Unblock(string exe, CleanContext ctx)
        {
            var program = PowerShellRunner.Quote(exe);
            Check(PowerShellRunner.Run(
                $"try {{ Get-NetFirewallRule -Group '{Group}' -ErrorAction Stop | Where-Object {{ ($_ | Get-NetFirewallApplicationFilter).Program -eq {program} }} | Remove-NetFirewallRule -ErrorAction Stop }} " +
                "catch { 'E' + [char]9 + $_.Exception.Message }", ctx));
        }

        static void Check(List<string> lines)
        {
            var error = lines.FirstOrDefault(l => l.StartsWith("E\t"));
            if (error != null) throw new InvalidOperationException(error.Substring(2));
        }
    }
}
