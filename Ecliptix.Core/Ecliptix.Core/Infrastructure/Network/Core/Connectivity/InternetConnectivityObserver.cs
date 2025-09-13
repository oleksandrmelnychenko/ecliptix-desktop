using System;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Core;
using Serilog;

namespace Ecliptix.Core.Infrastructure.Network.Core.Connectivity;

public sealed class InternetConnectivityObserver : IInternetConnectivityObserver
{
    public const string HttpClientName = "InternetConnectivityProbeClient";

    private readonly HttpClient _httpClient;
    private readonly IObservable<bool> _connectivityObservable;
    private readonly CompositeDisposable _disposables = new();

    private readonly BehaviorSubject<TimeSpan> _pollingIntervalSubject;

    public InternetConnectivityObserver(
        HttpClient httpClient,
        IScheduler uiScheduler,
        InternetConnectivityObserverOptions currentOptions)
    {
        _httpClient = httpClient;
        _pollingIntervalSubject = new BehaviorSubject<TimeSpan>(currentOptions.PollingInterval);

        _connectivityObservable = _pollingIntervalSubject
            .DistinctUntilChanged()
            .Select(interval =>
                    Observable.Timer(TimeSpan.Zero, interval, Scheduler.Default) // fresh timer
            )
            .Switch()
            .SelectMany(_ => ProbeConnectivityAsync(currentOptions, CancellationToken.None))
            .Scan((count: 0, state: true),
                (acc, isSuccess) => acc.state == isSuccess ? (acc.count + 1, acc.state) : (1, isSuccess))
            .Do(acc =>
            {
                if (!acc.state)
                {
                    Log.Information("Polling interval changed to 1 second due to connectivity failure");
                    _pollingIntervalSubject.OnNext(TimeSpan.FromSeconds(1));
                }
                else
                {
                    Log.Information("Polling interval restored to {Interval} due to connectivity success", currentOptions.PollingInterval);
                    _pollingIntervalSubject.OnNext(currentOptions.PollingInterval);
                }
            })
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

                if (response.IsSuccessStatusCode)
                {
                    Log.Debug("Connectivity probe succeeded for URL: {Url}", url);
                    return true;
                }

                Log.Warning("Connectivity probe failed (status code) for URL: {Url}", url);
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