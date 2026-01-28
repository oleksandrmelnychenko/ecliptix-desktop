using System.Net.NetworkInformation;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Ecliptix.Network.Infrastructure.Network.Abstractions.Core;

namespace Ecliptix.Network.Infrastructure.Network.Core.Connectivity;

public sealed class InternetConnectivityObserver : IInternetConnectivityObserver
{
    public const string HTTP_CLIENT_NAME = "InternetConnectivityProbeClient";
    private const int NETWORK_CHANGE_THROTTLE_MS = 400;
    private const int FAILURE_POLLING_SECONDS = 1;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IObservable<bool> _connectivityObservable;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
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

        IObservable<Unit> networkChangeObservable = Observable.Merge(
                Observable.FromEvent<NetworkAddressChangedEventHandler, EventPattern<EventArgs>>(
                        handler => (sender, e) => handler(new EventPattern<EventArgs>(sender, e)),
                        h => NetworkChange.NetworkAddressChanged += h,
                        h => NetworkChange.NetworkAddressChanged -= h)
                    .Select(_ => Unit.Default),

                Observable.FromEvent<NetworkAvailabilityChangedEventHandler, EventPattern<NetworkAvailabilityEventArgs>>(
                        handler => (sender, e) => handler(new EventPattern<NetworkAvailabilityEventArgs>(sender, e)),
                        h => NetworkChange.NetworkAvailabilityChanged += h,
                        h => NetworkChange.NetworkAvailabilityChanged -= h)
                    .Select(_ => Unit.Default)
            )
            .Throttle(TimeSpan.FromSeconds(NETWORK_CHANGE_THROTTLE_MS));

        IObservable<bool> probeObservable = pollingIntervalSubject
            .DistinctUntilChanged()
            .Select(interval => Observable.Timer(TimeSpan.Zero, interval, Scheduler.Default))
            .Switch()
            .Select(_ => Unit.Default)
            .Merge(_manualProbeTrigger)
            .Merge(networkChangeObservable)
            .SelectMany(_ => ProbeConnectivityAsync(_options, _cancellationTokenSource.Token));

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
                    ? TimeSpan.FromSeconds(FAILURE_POLLING_SECONDS)
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
        HttpClient httpClient = _httpClientFactory.CreateClient(HTTP_CLIENT_NAME);

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
                HttpResponseMessage response = await httpClient.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (Exception)
            {

            }
        }

        return false;
    }

    public IDisposable Subscribe(IObserver<bool> observer) =>
        _connectivityObservable.Subscribe(observer);

    public void Dispose()
    {
        if (!_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Cancel();
        }

        _cancellationTokenSource.Dispose();
        _manualProbeTrigger.Dispose();
    }
}
