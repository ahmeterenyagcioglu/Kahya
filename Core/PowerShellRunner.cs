using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Kahya.Core
{
    /// <summary>
    /// Windows PowerShell betiği çalıştırır ve çıktı satırlarını döndürür.
    /// Betikler sonuçları sekmeyle ayrılmış satırlar olarak yazar; böylece yerelleştirilmiş metin ayrıştırmaya gerek kalmaz.
    /// </summary>
    public static class PowerShellRunner
    {
        public static List<string> Run(string script, CleanContext ctx)
        {
            script = "[Console]::OutputEncoding = [Text.Encoding]::UTF8; $ProgressPreference = 'SilentlyContinue'\n" + script;
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            var lines = new List<string>();
            using (var p = Process.Start(psi))
            {
                p.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        lock (lines) lines.Add(e.Data.TrimEnd('\r', ' ')); // sondaki boş sekme alanları korunmalı
                };
                p.ErrorDataReceived += (s, e) => { };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                while (!p.WaitForExit(250))
                {
                    if (ctx.Token.IsCancellationRequested)
                    {
                        try { p.Kill(); } catch { }
                        ctx.Token.ThrowIfCancellationRequested();
                    }
                }
                p.WaitForExit();
            }
            lock (lines) return new List<string>(lines);
        }

        /// <summary>PowerShell tek tırnaklı metin.</summary>
        public static string Quote(string s) => "'" + (s ?? "").Replace("'", "''") + "'";
    }
}
