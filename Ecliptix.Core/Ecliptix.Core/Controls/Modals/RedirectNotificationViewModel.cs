using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Core.Services.Core.Localization;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Controls.Modals;

public class RedirectNotificationViewModel : ReactiveObject, IDisposable
{
    private readonly IDisposable? _timer;
    private readonly ILocalizationService _localizationService;
    private string? _cachedRedirectingTemplate;
    private readonly CompositeDisposable _disposables = new();
    
    [Reactive] public string Message { get; set; } 
    [Reactive] public double Progress { get; set; } = 0;
    [Reactive] public string CountdownText { get; set; }
    
    public RedirectNotificationViewModel(string message, int totalSeconds, Action onComplete, ILocalizationService localizationService)
    {
        _localizationService = localizationService;
        Message = message;
        
        
        int remainingSeconds = totalSeconds;
        
        TimeSpan updateInterval = TimeSpan.FromMilliseconds(5);
        Stopwatch stopwatch = Stopwatch.StartNew();
        
        IObservable<Unit> languageTrigger = Observable.FromEvent(
                handler => _localizationService.LanguageChanged += handler,
                handler => _localizationService.LanguageChanged -= handler)
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default);
        
        languageTrigger
            .Skip(1) 
            .Subscribe(_ => ClearStringCache())
            .DisposeWith(_disposables);
        
        Progress = 0.0;
        CountdownText = GetCachedRedirectingText(remainingSeconds);
        
        Observable.Interval(TimeSpan.FromMilliseconds(5))
            .StartWith(0)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                double elapsed = stopwatch.Elapsed.TotalSeconds;
                Progress = Math.Min(100.0, (elapsed / totalSeconds) * 100.0);
            })
            .DisposeWith(_disposables);
        
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
            .DisposeWith(_disposables);
        
        
        Observable.Timer(TimeSpan.FromSeconds(totalSeconds))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                Progress = 100.0;
                CountdownText = GetCachedRedirectingText(0);
                onComplete();
            })
            .DisposeWith(_disposables);

        Disposable.Create(() => stopwatch.Stop()).DisposeWith(_disposables);
        
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
        _disposables.Dispose();
    }
}