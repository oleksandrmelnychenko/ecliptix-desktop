using System;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Ecliptix.Core.Network;

public sealed class InternetConnectivityObserver : IObservable<bool>, IDisposable
{
    public const string HttpClientName = "InternetConnectivityProbeClient";

    private readonly HttpClient _httpClient;
    private readonly IObservable<bool> _connectivityObservable;
    private readonly CompositeDisposable _disposables = new();

    public InternetConnectivityObserver(
        HttpClient httpClient,
        IScheduler uiScheduler,
        InternetConnectivityObserverOptions currentOptions)
    {
        _httpClient = httpClient;

        _connectivityObservable = Observable
            .Timer(TimeSpan.Zero, currentOptions.PollingInterval, Scheduler.Default)
            .SelectMany(_ => ProbeConnectivityAsync(currentOptions, CancellationToken.None))
            .Scan((count: 0, state: true),
                (acc, isSuccess) => acc.state == isSuccess ? (acc.count + 1, acc.state) : (1, isSuccess))
            .Where(acc =>
            {
                (int count, bool state) = acc;
                int threshold = state ? currentOptions.SuccessThreshold : currentOptions.FailureThreshold;
                return count >= threshold;
            })
            .Select(acc => acc.state)
            .DistinctUntilChanged()
            .Do(status => Log.Information("Internet connectivity status changed to: {Status}", status))
            .ObserveOn(uiScheduler)
            .Replay(1)
            .RefCount();

        _connectivityObservable.Subscribe().DisposeWith(_disposables);
    }

    private async Task<bool> ProbeConnectivityAsync(InternetConnectivityObserverOptions options, CancellationToken ct)
    {
        foreach (string url in options.ProbeUrls)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                using CancellationTokenSource cts = new(options.ProbeTimeout);
                using CancellationTokenSource
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

                using HttpRequestMessage request = new(HttpMethod.Get, url);
                HttpResponseMessage response = await _httpClient.SendAsync(request, linkedCts.Token);

                if (!response.IsSuccessStatusCode) continue;

                Log.Debug("Connectivity probe succeeded for URL: {Url}", url);
                return true;
            }
            catch (OperationCanceledException)
            {
                Log.Warning("Connectivity probe timed out for URL: {Url}", url);
            }
            catch (HttpRequestException ex)
            {
                Log.Warning(ex, "Connectivity probe failed for URL: {Url}", url);
            }
        }

        Log.Debug("All connectivity probes failed");
        return false;
    }

    public IDisposable Subscribe(IObserver<bool> observer)
    {
        return _connectivityObservable.Subscribe(observer);
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}