using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Authentication.Constants;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Controls.Modals;

public class RedirectNotificationViewModel : ReactiveObject, IDisposable, IActivatableViewModel
{
    public ViewModelActivator Activator { get; } = new();
    
    private readonly ILocalizationService _localizationService;
    private string? _cachedRedirectingTemplate;
    private bool _disposed = false;
    
    [Reactive] public string Message { get; set; } 
    [Reactive] public double Progress { get; set; } = 0;
    [Reactive] public string CountdownText { get; set; }
    
    public RedirectNotificationViewModel(
        string message,
        int totalSeconds,
        Action onComplete,
        ILocalizationService localizationService)
    {
        _localizationService = localizationService;
        Message = message;
        int remainingSeconds = totalSeconds;
        Progress = 0.0;
        CountdownText = GetCachedRedirectingText(remainingSeconds);
        
        this.WhenActivated(disposables =>
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            IObservable<Unit> languageTrigger = Observable.FromEvent(
                    handler => _localizationService.LanguageChanged += handler,
                    handler => _localizationService.LanguageChanged -= handler)
                .DistinctUntilChanged()
                .Select(_ => Unit.Default)
                .StartWith(Unit.Default);
        
            languageTrigger
                .Skip(1) 
                .Subscribe(_ => ClearStringCache())
                .DisposeWith(disposables);
            
             Observable.Interval(TimeSpan.FromMilliseconds(5))
                        .StartWith(0)
                        .ObserveOn(RxApp.MainThreadScheduler)
                        .Subscribe(_ =>
                        {
                            double elapsed = stopwatch.Elapsed.TotalSeconds;
                            Progress = Math.Min(100.0, (elapsed / totalSeconds) * 100.0);
                        })
                        .DisposeWith(disposables);
             Observable.Interval(TimeSpan.FromSeconds(1))
                 .StartWith(0)
                 .ObserveOn(RxApp.MainThreadScheduler)
                 .Subscribe(_ =>
                 {
                     double elapsed = stopwatch.Elapsed.TotalSeconds;
                     int newRemaining = Math.Max(0, (int)Math.Ceiling(totalSeconds - elapsed));
                     if (newRemaining != remainingSeconds)
                     {
                         remainingSeconds = newRemaining;
                         CountdownText = GetCachedRedirectingText(remainingSeconds);
                     }
                 })
                 .DisposeWith(disposables);
             Observable.Timer(TimeSpan.FromSeconds(totalSeconds))
                 .ObserveOn(RxApp.MainThreadScheduler)
                 .Subscribe(_ =>
                 {
                     Progress = 100.0;
                     CountdownText = GetCachedRedirectingText(0);
                     onComplete();
                 })
                 .DisposeWith(disposables);
             Disposable.Create(() => stopwatch.Stop()).DisposeWith(disposables);

        });
    }
    
    private void ClearStringCache()
    {
        _cachedRedirectingTemplate = null;
    }
    
    private string GetCachedRedirectingText(int seconds)
    {
        _cachedRedirectingTemplate ??= _localizationService[AuthenticationConstants.RedirectingInSecondsKey];
        return string.Format(_cachedRedirectingTemplate, seconds);
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}