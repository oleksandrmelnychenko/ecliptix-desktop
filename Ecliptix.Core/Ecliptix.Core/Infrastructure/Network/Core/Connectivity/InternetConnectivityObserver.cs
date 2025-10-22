using System;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Core;

namespace Ecliptix.Core.Infrastructure.Network.Core.Connectivity;

internal sealed class InternetConnectivityObserver : IInternetConnectivityObserver
{
    public const string HttpClientName = "InternetConnectivityProbeClient";
    private const int NetworkChangeThrottleMs = 500;
    private const int FailurePollingSeconds = 1;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IObservable<bool> _connectivityObservable;
    private readonly CompositeDisposable _disposables = new();
    private readonly CancellationTokenSource _probeCancellationCts = new();
    private readonly Subject<Unit> _manualProbeTrigger = new();
    private readonly InternetConnectivityObserverOptions _options;

    public InternetConnectivityObserver(
        IHttpClientFactory httpClientFactory,
        IScheduler uiScheduler,
        InternetConnectivityObserverOptions currentOptions)
    {
        if (currentOptions == null)
        {
            throw new ArgumentNullException(nameof(currentOptions));
        }

        if (currentOptions.ProbeUrls == null || !currentOptions.ProbeUrls.Any())
        {
            throw new ArgumentException("ProbeUrls cannot be null or empty", nameof(currentOptions));
        }

        _httpClientFactory = httpClientFactory;
        _options = currentOptions;

        BehaviorSubject<TimeSpan> pollingIntervalSubject = new(currentOptions.PollingInterval);

        IObservable<Unit> networkChangeObservable = Observable
            .FromEvent<NetworkAddressChangedEventHandler, EventPattern<EventArgs>>(
                handler => (sender, e) => handler(new EventPattern<EventArgs>(sender, e)),
                h => NetworkChange.NetworkAddressChanged += new NetworkAddressChangedEventHandler((s, e) => h(s, e)),
                h => NetworkChange.NetworkAddressChanged -= new NetworkAddressChangedEventHandler((s, e) => h(s, e)))
            .Throttle(TimeSpan.FromMilliseconds(NetworkChangeThrottleMs))
            .Select(_ => Unit.Default);

        IObservable<bool> probeObservable = pollingIntervalSubject
            .DistinctUntilChanged()
            .Select(interval => Observable.Timer(TimeSpan.Zero, interval, Scheduler.Default))
            .Switch()
            .Select(_ => Unit.Default)
            .Merge(_manualProbeTrigger)
            .Merge(networkChangeObservable)
            .SelectMany(_ => ProbeConnectivityAsync(_options, _probeCancellationCts.Token));

        _connectivityObservable = probeObservable
            .Scan(
                seed: (count: 0, state: true),
                accumulator: (acc, isSuccess) =>
                    acc.state == isSuccess
                        ? (acc.count + 1, acc.state)
                        : (1, isSuccess))
            .Do(acc =>
            {
                TimeSpan currentInterval = pollingIntervalSubject.Value;
                TimeSpan desiredInterval = !acc.state
                    ? TimeSpan.FromSeconds(FailurePollingSeconds)
                    : currentOptions.PollingInterval;

                if (currentInterval != desiredInterval)
                {
                    pollingIntervalSubject.OnNext(desiredInterval);
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
            .ObserveOn(uiScheduler)
            .Replay(1)
            .RefCount();
    }

    private async Task<bool> ProbeConnectivityAsync(
        InternetConnectivityObserverOptions options,
        CancellationToken ct)
    {
        HttpClient httpClient = _httpClientFactory.CreateClient(HttpClientName);

        foreach (string url in options.ProbeUrls)
        {
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linkedCts.CancelAfter(options.ProbeTimeout);

                using HttpRequestMessage request = new(HttpMethod.Head, url);
                HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    public IDisposable Subscribe(IObserver<bool> observer) =>
        _connectivityObservable.Subscribe(observer);

    public void Dispose()
    {
        if (!_probeCancellationCts.IsCancellationRequested)
        {
            try
            {
                _probeCancellationCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _probeCancellationCts.Dispose();
        _manualProbeTrigger.Dispose();
        _disposables.Dispose();
    }
}
