using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class SpeedResult
    {
        public double PingMs;
        public double DownMbps;
        public double UpMbps;

        /// <summary>Sonucun ne işe yettiği, sade dille.</summary>
        public string Verdict =>
            DownMbps >= 100 ? T("Çok hızlı: 4K video, büyük indirmeler ve oyun rahat.")
            : DownMbps >= 25 ? T("İyi: 4K video ve görüntülü görüşme için yeterli.")
            : DownMbps >= 10 ? T("Orta: HD video olur; aynı anda birkaç kişi kullanırsa yavaşlar.")
            : DownMbps >= 3 ? T("Yavaş: video düşük kalitede açılır.")
            : T("Çok yavaş: modemi yeniden başlatmayı ya da kabloyla bağlanmayı dene.");
    }

    /// <summary>
    /// İnternet hız testi: Cloudflare'in ücretsiz test sunucusu (speed.cloudflare.com). Hesap ya da kurulum gerekmez;
    /// birkaç paralel bağlantıyla belirli süre indirip yükler.
    /// </summary>
    public static class SpeedTest
    {
        const string Base = "https://speed.cloudflare.com/";
        const int Streams = 4;

        public static async Task<SpeedResult> RunAsync(Action<string, double> progress, CancellationToken token)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, Streams * 2);
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Kahya/" + AppInfo.Version);
                var result = new SpeedResult();

                progress(T("Gecikme ölçülüyor…"), 0);
                var pings = new List<double>();
                for (int i = 0; i < 6; i++)
                {
                    var sw = Stopwatch.StartNew();
                    using (var r = await http.GetAsync(Base + "__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, token))
                        r.EnsureSuccessStatusCode();
                    pings.Add(sw.Elapsed.TotalMilliseconds);
                }
                result.PingMs = pings.Skip(1).OrderBy(x => x).ElementAt((pings.Count - 1) / 2); // ilki bağlantı kurma, ortanca

                result.DownMbps = await MeasureAsync(TimeSpan.FromSeconds(8), async (budget, count) =>
                {
                    using (var r = await http.GetAsync(Base + "__down?bytes=25000000", HttpCompletionOption.ResponseHeadersRead, budget))
                    using (var s = await r.Content.ReadAsStreamAsync())
                    {
                        var buffer = new byte[64 * 1024];
                        int n;
                        while ((n = await s.ReadAsync(buffer, 0, buffer.Length, budget)) > 0) count(n);
                    }
                }, (mbps, frac) => progress(T("İndirme: {0:0} Mbps", mbps), 0.1 + frac * 0.5), token);

                var payload = new byte[4 * 1024 * 1024];
                new Random().NextBytes(payload);
                result.UpMbps = await MeasureAsync(TimeSpan.FromSeconds(6), async (budget, count) =>
                {
                    using (var content = new ByteArrayContent(payload))
                    using (var r = await http.PostAsync(Base + "__up", content, budget))
                        if (r.IsSuccessStatusCode) count(payload.Length);
                }, (mbps, frac) => progress(T("Yükleme: {0:0} Mbps", mbps), 0.6 + frac * 0.4), token);

                progress(null, 1);
                return result;
            }
        }

        /// <summary>
        /// Süre dolana kadar <see cref="Streams"/> paralel akışla işi tekrarlar; aktarılan bayttan Mbps hesaplar.
        /// İlk saniye (TCP ısınması) hesaba katılmaz.
        /// </summary>
        static async Task<double> MeasureAsync(TimeSpan duration, Func<CancellationToken, Action<long>, Task> work,
                                               Action<double, double> report, CancellationToken token)
        {
            long bytes = 0, warmBytes = 0;
            var sw = Stopwatch.StartNew();
            var warm = TimeSpan.FromSeconds(1);
            bool warmed = false;
            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                budget.CancelAfter(duration);
                void Count(long n)
                {
                    long total = Interlocked.Add(ref bytes, n);
                    if (!warmed && sw.Elapsed >= warm) { warmed = true; Interlocked.Exchange(ref warmBytes, total); }
                }
                var reporter = Task.Run(async () =>
                {
                    while (!budget.IsCancellationRequested)
                    {
                        await Task.Delay(500).ConfigureAwait(false);
                        report(Mbps(Interlocked.Read(ref bytes) - Interlocked.Read(ref warmBytes), sw.Elapsed - (warmed ? warm : TimeSpan.Zero)),
                               Math.Min(1, sw.Elapsed.TotalSeconds / duration.TotalSeconds));
                    }
                });
                var streams = Enumerable.Range(0, Streams).Select(async _ =>
                {
                    while (!budget.IsCancellationRequested)
                    {
                        try { await work(budget.Token, Count).ConfigureAwait(false); }
                        catch (OperationCanceledException) { break; }
                        catch (Exception) when (!budget.IsCancellationRequested) { await Task.Delay(200).ConfigureAwait(false); }
                        catch { break; }
                    }
                }).ToArray();
                await Task.WhenAll(streams);
                token.ThrowIfCancellationRequested();
                await reporter;
            }
            var elapsed = sw.Elapsed - (warmed ? warm : TimeSpan.Zero);
            if (Interlocked.Read(ref bytes) == 0) throw new InvalidOperationException(T("Test sunucusuna ulaşılamadı."));
            return Mbps(bytes - warmBytes, elapsed);
        }

        static double Mbps(long bytes, TimeSpan time) => time.TotalSeconds <= 0 ? 0 : bytes * 8 / time.TotalSeconds / 1e6;
    }
}
