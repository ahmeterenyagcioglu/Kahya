using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Uygulamalar › Başlangıç: son 90 günün açılış süreleri ve başlangıç değişikliklerinin etkisi
    public partial class MainWindow
    {
        List<BootRecord> _boots = new List<BootRecord>();
        DateTime? _bootChange;

        void LoadBootChart()
        {
            try { _boots = BootTimes.LoadTimes().OrderBy(b => b.Date).ToList(); } catch { _boots = new List<BootRecord>(); }
            if (_boots.Count < 2 || _startupMode != "apps")
            {
                BootCard.Visibility = Visibility.Collapsed;
                return;
            }
            BootCard.Visibility = Visibility.Visible;
            var last = _boots[_boots.Count - 1];
            var month = _boots.Where(b => b.Date >= DateTime.Now.AddDays(-30)).ToList();
            BootValue.Text = T("{0:0} sn", last.MainPathMs / 1000.0);
            BootSub.Text = T("son açılış · 30 gün ortalaması {0:0} sn", month.Average(b => b.MainPathMs) / 1000.0);

            // Açılışta başlayanlardan biri kapatıldıktan sonra açılış hızlandı mı?
            _bootChange = null;
            BootInsight.Text = "";
            var change = History.Load()
                .Where(h => h.Kind == "change" && !h.Undone && (h.Undo ?? "").StartsWith("startup|") && h.Undo.EndsWith("|1"))
                .FirstOrDefault(h => h.Date > _boots[0].Date);
            if (change != null)
            {
                var before = _boots.Where(b => b.Date < change.Date).Reverse().Take(10).ToList();
                var after = _boots.Where(b => b.Date > change.Date).ToList();
                if (before.Count >= 3 && after.Count >= 3)
                {
                    _bootChange = change.Date;
                    double diff = (before.Average(b => b.MainPathMs) - after.Average(b => b.MainPathMs)) / 1000.0;
                    BootInsight.Text = diff >= 2
                        ? T("{0:dd.MM}'deki değişiklikten ({1}) sonra açılış ortalama {2:0} sn hızlandı.", change.Date, change.Title, diff)
                        : T("{0:dd.MM}'deki değişiklikten ({1}) sonra açılış süresi belirgin değişmedi.", change.Date, change.Title);
                    BootInsight.SetResourceReference(TextBlock.ForegroundProperty, diff >= 2 ? "Good" : "Muted");
                }
            }
            DrawBootChart();
        }

        void BootChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawBootChart();

        void DrawBootChart()
        {
            BootChart.Children.Clear();
            double w = BootChart.ActualWidth, h = BootChart.ActualHeight;
            if (_boots.Count < 2 || w < 20 || h < 10) return;

            var start = _boots[0].Date;
            double span = Math.Max(1, (_boots[_boots.Count - 1].Date - start).TotalHours);
            // Dikey eksen en kısa ve en uzun açılışa göre: farklar görünsün.
            double lo = _boots.Min(b => b.MainPathMs), hi = _boots.Max(b => b.MainPathMs), pad = 5;
            double range = Math.Max(hi - lo, 5000);
            lo = Math.Max(0, lo - range * 0.25);
            hi = lo + range * 1.5;
            double Y(double ms) => h - pad - (ms - lo) / (hi - lo) * (h - 2 * pad);
            Point P(BootRecord b) => new Point(pad + (b.Date - start).TotalHours / span * (w - 2 * pad), Y(b.MainPathMs));

            var accent = (Brush)FindResource("Accent");
            var faint = (Brush)FindResource("Faint");

            double avgY = Y(_boots.Average(b => b.MainPathMs));
            BootChart.Children.Add(new Line { X1 = 0, X2 = w, Y1 = avgY, Y2 = avgY, Stroke = faint, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } });

            if (_bootChange != null)
            {
                double x = pad + (_bootChange.Value - start).TotalHours / span * (w - 2 * pad);
                BootChart.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = h, Stroke = (Brush)FindResource("Good"), StrokeThickness = 1.5,
                                                  ToolTip = T("Başlangıç değişikliği") });
            }

            var line = new Polyline { Stroke = accent, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            foreach (var b in _boots) line.Points.Add(P(b));
            BootChart.Children.Add(line);

            foreach (var b in _boots)
            {
                var p = P(b);
                var dot = new Ellipse { Width = 7, Height = 7, Fill = accent, ToolTip = T("{0:dd.MM HH:mm} · {1:0} sn", b.Date, b.MainPathMs / 1000.0) };
                Canvas.SetLeft(dot, p.X - 3.5);
                Canvas.SetTop(dot, p.Y - 3.5);
                BootChart.Children.Add(dot);
            }
        }
    }
}
