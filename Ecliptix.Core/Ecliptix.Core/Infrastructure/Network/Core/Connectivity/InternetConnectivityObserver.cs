using System;
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
using Serilog;

namespace Ecliptix.Core.Infrastructure.Network.Core.Connectivity
{
    public sealed class InternetConnectivityObserver : IInternetConnectivityObserver
    {
        public const string HttpClientName = "InternetConnectivityProbeClient";

        private readonly HttpClient _httpClient;
        private readonly IObservable<bool> _connectivityObservable;
        private readonly CompositeDisposable _disposables = new();
        private readonly Subject<Unit> _manualProbeTrigger = new();

        public InternetConnectivityObserver(
            HttpClient httpClient,
            IScheduler uiScheduler,
            InternetConnectivityObserverOptions currentOptions)
        {
            _httpClient = httpClient;
            var pollingIntervalSubject = new BehaviorSubject<TimeSpan>(currentOptions.PollingInterval);

            IObservable<bool> adapterObservable = Observable
                .FromEvent<NetworkAddressChangedEventHandler, EventPattern<EventArgs>>(
                    handler => (sender, e) => handler(new EventPattern<EventArgs>(sender, e)),
                    h => NetworkChange.NetworkAddressChanged += new NetworkAddressChangedEventHandler((s, e) => h(s, e)),
                    h => NetworkChange.NetworkAddressChanged -= new NetworkAddressChangedEventHandler((s, e) => h(s, e)))
                .Throttle(TimeSpan.FromMilliseconds(500)) 
                .Do(_ =>
                {
                    Log.Information("Network configuration changed, triggering connectivity probe");
                    _manualProbeTrigger.OnNext(Unit.Default);
                })
                .Select(_ => true); 
            
            IObservable<bool> probeObservable =
                pollingIntervalSubject
                    .DistinctUntilChanged()
                    .Select(interval =>
                        Observable.Timer(TimeSpan.Zero, interval, Scheduler.Default))
                    .Switch()
                    .Select(_ => Unit.Default)
                    .Merge(_manualProbeTrigger)
                    .SelectMany(_ => ProbeConnectivityAsync(currentOptions, CancellationToken.None));
            
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
                        ? TimeSpan.FromSeconds(1) 
                        : currentOptions.PollingInterval;
    
                    if (currentInterval != desiredInterval)
                    {
                        if (!acc.state)
                        {
                            Log.Information("Polling interval changed to 1 second due to connectivity failure");
                        }
                        else
                        {
                            Log.Information("Polling interval restored to {Interval} due to connectivity success", 
                                currentOptions.PollingInterval);
                        }
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
                .Do(status => Log.Information("Internet connectivity status changed to: {Status}", status))
                .ObserveOn(uiScheduler)
                .Replay(1)
                .RefCount();

            _connectivityObservable.Subscribe().DisposeWith(_disposables);

            adapterObservable.Subscribe().DisposeWith(_disposables);
        }

        private async Task<bool> ProbeConnectivityAsync(
            InternetConnectivityObserverOptions options,
            CancellationToken ct)
        {
            foreach (string url in options.ProbeUrls)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    using CancellationTokenSource cts = new CancellationTokenSource(options.ProbeTimeout);
                    using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

                    using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
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
                catch (HttpRequestException)
                {
                    Log.Warning("Connectivity probe failed for URL: {Url}", url);
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
}