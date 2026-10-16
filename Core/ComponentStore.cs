using System;
using System.Collections.Generic;
using System.Linq;

namespace Kahya.Core
{
    /// <summary>"dism /AnalyzeComponentStore" çıktısının özeti.</summary>
    public sealed class ComponentStore
    {
        public string ExplorerSize;
        public string ActualSize;
        public string Shared;
        public string Backups;
        public int ReclaimablePackages;
        public bool CleanupRecommended;

        static readonly HashSet<string> Yes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Yes", "Evet", "Ja", "Oui", "Sí", "Si", "Sim", "Sì", "Tak", "Да", "Igen", "Ano", "Kyllä" };
        static readonly HashSet<string> No = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "No", "Hayır", "Nein", "Non", "Não", "Nie", "Нет", "Nem", "Ne", "Ei", "Nej", "Nee" };

        /// <summary>
        /// DISM'in çıktısı Windows'un diline göre çevrilir; bu yüzden satırlar başlıklarından değil sırasından okunur:
        /// "(WinSxS)" satırından sonraki "ad : değer" satırları her dilde aynı sıradadır. Okunamazsa null.
        /// </summary>
        public static ComponentStore Parse(IEnumerable<string> lines)
        {
            var values = lines
                .SkipWhile(l => l.IndexOf("(WinSxS)", StringComparison.OrdinalIgnoreCase) < 0)
                .Skip(1)
                .Where(l => l.Contains(" : "))
                .Select(l => l.Substring(l.IndexOf(':') + 1).Trim())
                .ToList();
            if (values.Count < 8) return null;

            int.TryParse(values[6], out var packages);
            var recommended = values[7];
            return new ComponentStore
            {
                ExplorerSize = values[0],
                ActualSize = values[1],
                Shared = values[2],
                Backups = values[3],
                ReclaimablePackages = packages,
                CleanupRecommended = Yes.Contains(recommended) || (!No.Contains(recommended) && packages > 0),
            };
        }
    }
}
