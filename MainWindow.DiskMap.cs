using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Temizlik › Disk analizi: klasörleri kapladıkları yere göre kutular (ağaç haritası). Kutuya tıklayınca içine girer.
    public partial class MainWindow
    {
        bool _diskMap;
        List<DiskRow> _mapRows;

        static readonly Color[] MapColors =
        {
            Color.FromRgb(0x5B, 0x6C, 0xF0), Color.FromRgb(0x2E, 0xA0, 0x8F), Color.FromRgb(0xE0, 0x8A, 0x2E), Color.FromRgb(0xC2, 0x4E, 0x8B),
            Color.FromRgb(0x3E, 0x8E, 0xD0), Color.FromRgb(0x8A, 0x63, 0xD2), Color.FromRgb(0x5E, 0xA3, 0x4A), Color.FromRgb(0xD0, 0x5A, 0x4E),
        };

        void DiskMap_Click(object sender, RoutedEventArgs e)
        {
            _diskMap = !_diskMap;
            ShowDisk();
        }

        void ShowDiskMap(List<DiskRow> rows)
        {
            bool folders = _diskResult != null && _diskMode == DiskMode.Folders;
            DiskMapBtn.Visibility = folders ? Visibility.Visible : Visibility.Collapsed;
            DiskMapBtn.Content = _diskMap ? T("Liste") : T("Harita");
            bool show = _diskMap && folders && rows != null && rows.Count > 0;
            DiskMap.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (folders) DiskScroll.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            _mapRows = show ? rows : null;
            DrawDiskMap();
        }

        void DiskMap_SizeChanged(object sender, SizeChangedEventArgs e) => DrawDiskMap();

        void DrawDiskMap()
        {
            DiskMap.Children.Clear();
            if (_mapRows == null) return;
            double w = DiskMap.ActualWidth, h = DiskMap.ActualHeight;
            if (w < 50 || h < 50) return;

            // Çok küçükler tek kutuda: "Diğer".
            var rows = _mapRows.Where(r => r.Percent >= 0.3).Take(60).ToList();
            double rest = _mapRows.Except(rows).Sum(r => r.Percent);
            var values = rows.Select(r => r.Percent).ToList();
            if (rest > 0) values.Add(rest);

            var rects = Treemap.Layout(values, new Rect(0, 0, w, h));
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                if (r.IsEmpty || r.Width < 2 || r.Height < 2) continue;
                var row = i < rows.Count ? rows[i] : null;
                var color = row == null ? Color.FromRgb(0x80, 0x80, 0x80) : MapColors[i % MapColors.Length];
                var box = new Border
                {
                    Width = Math.Max(0, r.Width - 3), Height = Math.Max(0, r.Height - 3),
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(row?.IsFile == true ? (byte)0x90 : (byte)0xD0, color.R, color.G, color.B)),
                    Cursor = row?.Node != null ? Cursors.Hand : Cursors.Arrow,
                    ToolTip = row == null ? T("Diğer küçük klasörler · {0}", Format.Percent(rest, "0.0"))
                                          : $"{row.FullPath}\n{row.SizeText} · {row.Sub}",
                    Padding = new Thickness(8, 6, 6, 4),
                };
                if (r.Width > 70 && r.Height > 34)
                {
                    var text = new StackPanel();
                    text.Children.Add(new TextBlock
                    {
                        Text = row?.Name ?? T("Diğer"), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });
                    if (r.Height > 50)
                        text.Children.Add(new TextBlock
                        {
                            Text = row?.SizeText ?? Format.Percent(rest, "0.0"), Foreground = new SolidColorBrush(Color.FromArgb(0xD8, 255, 255, 255)),
                            FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
                        });
                    box.Child = text;
                }
                if (row?.Node != null)
                    box.MouseLeftButtonUp += (s, e) => { _diskCurrent = row.Node; ShowDisk(); };
                Canvas.SetLeft(box, r.X);
                Canvas.SetTop(box, r.Y);
                DiskMap.Children.Add(box);
            }
        }
    }
}
