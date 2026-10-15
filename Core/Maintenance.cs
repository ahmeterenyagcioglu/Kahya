using System;
using System.Collections.Generic;
using System.Linq;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class MaintenanceResult
    {
        public long Freed;
        public int Updated, UpdateFailed;
        public int DriversRemoved;
        public long DriverBytes;
        public List<string> Failures = new List<string>();

        public string Summary()
        {
            var parts = new List<string> { T("{0} boşaltıldı", Format.Bytes(Freed + DriverBytes)) };
            if (Updated + UpdateFailed > 0)
                parts.Add(UpdateFailed > 0
                    ? T("{0} uygulama güncellendi ({1} başarısız)", Updated, UpdateFailed)
                    : T("{0} uygulama güncellendi", Updated));
            if (DriversRemoved > 0)
                parts.Add(T("{0} eski sürücü kaldırıldı", DriversRemoved));
            return string.Join(", ", parts);
        }
    }

    /// <summary>Tek tıkla / otomatik bakımda yapılacaklar.</summary>
    public sealed class MaintenancePlan
    {
        public List<CleanItem> CleanItems = new List<CleanItem>();
        public bool Updates, Drivers, Optimize;
        /// <summary>Güncellemeden önce geri yükleme noktası denensin mi (otomatik modda; arayüz bunu kendisi sorar).</summary>
        public bool RestorePoint;
        public ISet<string> IgnoredUpdates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Kayıtlı ayarlardan (otomatik bakım için).</summary>
        public static MaintenancePlan FromSettings(IEnumerable<CleanItem> items) => new MaintenancePlan
        {
            CleanItems = Settings.MaintenanceClean ? items.Where(Settings.InMaintenance).ToList() : new List<CleanItem>(),
            Updates = Settings.MaintenanceUpdates,
            Drivers = Settings.MaintenanceDrivers,
            Optimize = Settings.MaintenanceOptimize,
            RestorePoint = Settings.RestorePointBeforeUpdates,
            IgnoredUpdates = Settings.IgnoredUpdates,
        };

        public bool IsEmpty => CleanItems.Count == 0 && !Updates && !Drivers && !Optimize;
    }

    /// <summary>
    /// Tek tıkla bakım: seçili temizlik + uygulama güncellemeleri + eski sürücü paketleri + sürücü optimizasyonu.
    /// Hem "Tek tıkla bakım" butonu hem otomatik görev (/auto) bunu kullanır.
    /// </summary>
    public static class Maintenance
    {
        public static MaintenanceResult Run(MaintenancePlan plan, CleanEngine engine, CleanContext ctx, Action<string> stage = null)
        {
            var result = new MaintenanceResult();
            bool clean = plan.CleanItems.Count > 0;
            int steps = (clean ? 1 : 0) + (plan.Updates ? 1 : 0) + (plan.Drivers ? 1 : 0) + (plan.Optimize ? 1 : 0), step = 0;
            string Stage(string name) => T("Adım {0}/{1}: {2}", ++step, steps, name);

            if (clean)
            {
                var title = Stage(T("Temizlik"));
                stage?.Invoke(title);
                ctx.Log("━━ " + title);
                result.Freed = engine.Clean(plan.CleanItems, ctx.Token);
            }

            if (plan.Updates)
            {
                var title = Stage(T("Uygulama güncellemeleri"));
                stage?.Invoke(title);
                ctx.Log("━━ " + title);
                try
                {
                    var all = Winget.ListUpgrades(ctx);
                    var skipped = all.Where(p => plan.IgnoredUpdates.Contains(p.Id)).ToList();
                    var packages = all.Except(skipped).OrderBy(p => p.Id == "Microsoft.AppInstaller").ToList();
                    if (skipped.Count > 0)
                        ctx.Log("   " + T("Atlanan: {0}", string.Join(", ", skipped.Select(p => p.Name))));
                    if (packages.Count == 0)
                    {
                        ctx.Log("   " + T("Her şey güncel ✔"));
                    }
                    else
                    {
                        if (plan.RestorePoint)
                        {
                            ctx.Log("   " + T("Geri yükleme noktası oluşturuluyor…"));
                            var err = RestorePoint.Create(T("Kahya - güncelleme öncesi"), ctx);
                            ctx.Log(err == null ? "   ✔ " + T("Geri yükleme noktası oluşturuldu.") : "   ! " + T("Geri yükleme noktası oluşturulamadı: {0}", err));
                        }
                        for (int i = 0; i < packages.Count; i++)
                        {
                            var p = packages[i];
                            stage?.Invoke(T("{0} ({1}/{2}): {3}", title, i + 1, packages.Count, p.Name));
                            ctx.Log($"▶ {p.Name}  {p.Version} → {p.Available}");
                            if (Winget.Upgrade(p, ctx)) { result.Updated++; ctx.Log("   ✔ " + T("{0} güncellendi", p.Name)); }
                            else { result.UpdateFailed++; result.Failures.Add($"{p.Name}: {p.Error}"); }
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    ctx.Log("   ! " + T("Güncellemeler kontrol edilemedi: {0}", ex.Message));
                }
            }

            if (plan.Drivers)
            {
                var title = Stage(T("Eski sürücü paketleri"));
                stage?.Invoke(title);
                ctx.Log("━━ " + title);
                try
                {
                    var old = SystemCare.OldDrivers(ctx);
                    if (old.Count == 0) ctx.Log("   " + T("Eski sürücü paketi yok ✔"));
                    foreach (var d in old)
                    {
                        ctx.Log($"▶ {d.PublishedName} — {d.Title} v{d.Version}");
                        if (SystemCare.DeleteDriver(d, ctx)) { result.DriversRemoved++; result.DriverBytes += d.Size; }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    ctx.Log("   ! " + T("Sürücüler okunamadı: {0}", ex.Message));
                }
            }

            if (plan.Optimize)
            {
                var title = Stage(T("Sürücü optimizasyonu"));
                stage?.Invoke(title);
                ctx.Log("━━ " + title);
                var physical = DriveDetector.PhysicalLetters();
                var letters = physical != null ? string.Join(" ", physical.OrderBy(l => l)) : (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:");
                ProcessRunner.Run("defrag.exe", letters + " /O /U", ctx, logOutput: true);
            }

            return result;
        }
    }
}
