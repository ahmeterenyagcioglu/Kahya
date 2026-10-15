using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class RestorePointInfo : INotifyPropertyChanged
    {
        public int Sequence { get; set; }
        public string Description { get; set; }
        public DateTime Created { get; set; }
        public int Type { get; set; }

        public string TypeText =>
            Type == 0 ? T("program kurulumu") :
            Type == 1 ? T("program kaldırma") :
            Type == 10 ? T("sürücü kurulumu") :
            Type == 12 ? T("ayar değişikliği") :
            Type == 13 ? T("iptal edilmiş işlem") : T("sistem");

        public string Details => T("{0:dd.MM.yyyy HH:mm} · {1}", Created, TypeText);

        bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; Notify(nameof(IsChecked)); } }
        }

        string _status = "";
        public string Status
        {
            get => _status;
            set { _status = value; Notify(nameof(Status)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public sealed class ShadowStorageInfo
    {
        public long Used;
        public long Max;
        public long VolumeSize;

        /// <summary>Sınırın diskin yüzde kaçı olduğu; sınırsızsa ya da bilinmiyorsa -1.</summary>
        public double MaxPercent => VolumeSize > 0 && Max > 0 && Max < VolumeSize ? Max * 100.0 / VolumeSize : -1;
    }

    /// <summary>
    /// Sistem sürücüsündeki geri yükleme noktaları: listeleme, tek tek silme ve kapladıkları alanın sınırı.
    /// Hepsi yönetici ister.
    /// </summary>
    public static class RestorePoints
    {
        [DllImport("srclient.dll")]
        static extern int SRRemoveRestorePoint(int sequenceNumber);

        public static List<RestorePointInfo> List()
        {
            var list = new List<RestorePointInfo>();
            using (var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\default"),
                       new ObjectQuery("SELECT SequenceNumber, Description, CreationTime, RestorePointType FROM SystemRestore")))
            using (var items = searcher.Get())
            {
                foreach (ManagementBaseObject o in items)
                {
                    DateTime created;
                    try { created = ManagementDateTimeConverter.ToDateTime(o["CreationTime"] as string); }
                    catch { created = DateTime.MinValue; }
                    list.Add(new RestorePointInfo
                    {
                        Sequence = Convert.ToInt32(o["SequenceNumber"]),
                        Description = (o["Description"] as string ?? "").Trim(),
                        Created = created,
                        Type = Convert.ToInt32(o["RestorePointType"] ?? 0),
                    });
                }
            }
            return list.OrderByDescending(r => r.Created).ToList();
        }

        /// <summary>Başarılıysa null, değilse hata mesajı.</summary>
        public static string Delete(RestorePointInfo r)
        {
            int code = SRRemoveRestorePoint(r.Sequence);
            return code == 0 ? null : new System.ComponentModel.Win32Exception(code).Message;
        }

        static string SystemLetter() =>
            Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd('\\'); // "C:"

        /// <summary>Gölge kopyaların (geri yükleme noktaları) sistem sürücüsünde kullandığı ve en fazla kullanabileceği alan.</summary>
        public static ShadowStorageInfo Storage()
        {
            var info = new ShadowStorageInfo();
            var letter = SystemLetter();
            string volumeId = null;
            using (var s = new ManagementObjectSearcher($"SELECT DeviceID, Capacity FROM Win32_Volume WHERE DriveLetter = '{letter}'"))
            using (var items = s.Get())
                foreach (ManagementBaseObject o in items)
                {
                    volumeId = VolumeGuid(o["DeviceID"] as string);
                    info.VolumeSize = Convert.ToInt64(o["Capacity"] ?? 0L);
                }
            if (volumeId == null) return info;

            using (var s = new ManagementObjectSearcher("SELECT Volume, UsedSpace, MaxSpace FROM Win32_ShadowStorage"))
            using (var items = s.Get())
                foreach (ManagementBaseObject o in items)
                {
                    if (VolumeGuid(o["Volume"] as string) != volumeId) continue;
                    info.Used = Convert.ToInt64(o["UsedSpace"] ?? 0L);
                    info.Max = Convert.ToInt64(o["MaxSpace"] ?? 0L);
                }
            return info;
        }

        static string VolumeGuid(string s)
        {
            if (s == null) return null;
            int i = s.IndexOf("Volume{", StringComparison.OrdinalIgnoreCase);
            int j = i >= 0 ? s.IndexOf('}', i) : -1;
            return j > i ? s.Substring(i, j - i + 1).ToLowerInvariant() : null;
        }

        /// <summary>Geri yükleme noktalarının kullanabileceği alanı diskin yüzde <paramref name="percent"/>'i ile sınırlar. Sınır küçülürse en eski noktalar silinir.</summary>
        public static int SetLimit(int percent, CleanContext ctx)
        {
            var letter = SystemLetter();
            return ProcessRunner.Run("vssadmin.exe", $"resize shadowstorage /for={letter} /on={letter} /maxsize={percent}%", ctx, logOutput: true);
        }
    }
}
