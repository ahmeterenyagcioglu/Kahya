using System;
using System.Collections.Generic;
using System.Linq;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class FileTypeGroup
    {
        public string Key;
        public string Glyph;   // Segoe MDL2 / Fluent simge kodu
        public string Name;
        public long Size;
        public int Count;
        public List<KeyValuePair<string, ExtStat>> Extensions = new List<KeyValuePair<string, ExtStat>>();
    }

    /// <summary>Dosya uzantılarını Video, Resim, Müzik… gibi kategorilere ayırır.</summary>
    public static class FileTypes
    {
        static readonly (string key, string glyph, string name, string exts)[] Categories =
        {
            ("video", "", "Videolar", "mp4 mkv avi mov wmv flv webm m4v mpg mpeg ts m2ts 3gp vob"),
            ("image", "", "Resimler", "jpg jpeg png gif bmp webp heic heif tif tiff raw cr2 cr3 nef arw dng psd svg ico avif"),
            ("audio", "", "Müzik ve ses", "mp3 wav flac aac ogg m4a wma opus aiff mid"),
            ("docs", "", "Belgeler", "pdf doc docx xls xlsx ppt pptx odt ods odp txt rtf csv md epub one"),
            ("archive", "", "Arşivler ve disk görüntüleri", "zip rar 7z tar gz bz2 xz zst cab iso img vhd vhdx vmdk wim esd"),
            ("setup", "", "Programlar ve kurulum dosyaları", "exe msi msix msixbundle appx appxbundle dll sys ocx msp"),
            ("game", "", "Oyun verisi", "pak upk vpk bsa ba2 bundle assets ucas utoc forge sga big wad blob"),
            ("dev", "", "Geliştirici dosyaları", "cs js ts py java cpp c h hpp go rs json xml yml yaml lock pdb obj lib jar class node map so pack sst wasm whl"),
            ("system", "", "Sistem ve önbellek", "tmp log etl evtx dmp db sqlite dat bin cache idx pf mui cat manifest ldb"),
        };

        static readonly Dictionary<string, string> ExtToCategory = Build();

        static Dictionary<string, string> Build()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in Categories)
                foreach (var e in c.exts.Split(' '))
                    map["." + e] = c.key;
            return map;
        }

        public static List<FileTypeGroup> Group(Dictionary<string, ExtStat> extensions)
        {
            var groups = Categories.ToDictionary(c => c.key, c => new FileTypeGroup { Key = c.key, Glyph = c.glyph, Name = T(c.name) });
            var other = new FileTypeGroup { Key = "other", Glyph = "", Name = T("Diğer") };

            foreach (var e in extensions)
            {
                var g = ExtToCategory.TryGetValue(e.Key, out var key) ? groups[key] : other;
                g.Size += e.Value.Size;
                g.Count += e.Value.Count;
                g.Extensions.Add(new KeyValuePair<string, ExtStat>(e.Key.ToLowerInvariant(), e.Value));
            }

            var result = groups.Values.Concat(new[] { other }).Where(g => g.Count > 0).OrderByDescending(g => g.Size).ToList();
            foreach (var g in result)
                g.Extensions = g.Extensions.OrderByDescending(e => e.Value.Size).ToList();
            return result;
        }
    }
}
