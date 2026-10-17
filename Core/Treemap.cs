using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Kahya.Core
{
    /// <summary>
    /// Kare oranlı ağaç haritası (squarified treemap, Bruls ve ark. 2000): her öğe büyüklüğüyle orantılı bir dikdörtgen
    /// alır; dikdörtgenler kareye olabildiğince yakın tutulur ki adlar okunabilsin.
    /// </summary>
    public static class Treemap
    {
        /// <summary>Büyükten küçüğe sıralı değerler için dikdörtgenler (aynı sırayla). Sıfır ve eksi değerler boş dikdörtgen alır.</summary>
        public static List<Rect> Layout(IList<double> values, Rect area)
        {
            var result = Enumerable.Repeat(Rect.Empty, values.Count).ToList();
            double total = values.Where(v => v > 0).Sum();
            if (total <= 0 || area.Width <= 0 || area.Height <= 0) return result;

            // Değerleri alana ölçekle.
            double scale = area.Width * area.Height / total;
            var items = values.Select((v, i) => (index: i, area: Math.Max(0, v) * scale)).Where(x => x.area > 0).ToList();

            var free = area;
            int start = 0;
            while (start < items.Count)
            {
                double side = Math.Min(free.Width, free.Height);
                var row = new List<(int index, double area)> { items[start] };
                int next = start + 1;
                while (next < items.Count)
                {
                    var trial = new List<(int, double)>(row) { items[next] };
                    if (Worst(trial, side) > Worst(row, side)) break;
                    row = trial;
                    next++;
                }
                free = Place(row, free, result);
                start = next;
            }
            return result;
        }

        static double Worst(List<(int index, double area)> row, double side)
        {
            double sum = row.Sum(r => r.area), max = row.Max(r => r.area), min = row.Min(r => r.area);
            double s2 = side * side, sum2 = sum * sum;
            return Math.Max(s2 * max / sum2, sum2 / (s2 * min));
        }

        /// <summary>Satırı alanın kısa kenarı boyunca yerleştirir, kalan alanı döner.</summary>
        static Rect Place(List<(int index, double area)> row, Rect free, List<Rect> result)
        {
            double sum = row.Sum(r => r.area);
            if (free.Width >= free.Height)
            {
                // Sol şerit: genişlik = toplam / yükseklik
                double w = free.Height > 0 ? sum / free.Height : 0, y = free.Y;
                foreach (var r in row)
                {
                    double h = w > 0 ? r.area / w : 0;
                    result[r.index] = new Rect(free.X, y, w, h);
                    y += h;
                }
                return new Rect(free.X + w, free.Y, Math.Max(0, free.Width - w), free.Height);
            }
            else
            {
                double h = free.Width > 0 ? sum / free.Width : 0, x = free.X;
                foreach (var r in row)
                {
                    double w = h > 0 ? r.area / h : 0;
                    result[r.index] = new Rect(x, free.Y, w, h);
                    x += w;
                }
                return new Rect(free.X, free.Y + h, free.Width, Math.Max(0, free.Height - h));
            }
        }
    }
}
