using System;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Controls.Modals;

public class RedirectNotificationViewModel : ReactiveObject, IDisposable
{
    private readonly IDisposable? _timer;
    private readonly Action _onComplete;
    
    [Reactive] public string Message { get; set; } = string.Empty;
    [Reactive] public double Progress { get; set; } = 0;
    [Reactive] public string CountdownText { get; set; } = string.Empty;
    
    public RedirectNotificationViewModel(string message, int totalSeconds, Action onComplete)
    {
        Message = message;
        _onComplete = onComplete;
        
        int remainingSeconds = totalSeconds;
        
        _timer = Observable.Interval(TimeSpan.FromSeconds(0.1))
            .Take(totalSeconds * 10)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(tick =>
            {
                double elapsed = tick * 0.1;
                Progress = (elapsed / totalSeconds) * 100;
                
                int newRemaining = totalSeconds - (int)Math.Ceiling(elapsed);
                if (newRemaining != remainingSeconds)
                {
                    remainingSeconds = newRemaining;
                    CountdownText = $"Redirecting in {remainingSeconds} seconds...";
                }
                
                if (tick >= (totalSeconds * 10) - 1)
                {
                    _onComplete();
                }
            });
        
        CountdownText = $"Redirecting in {totalSeconds} seconds...";
    }
    
    public void Dispose()
    {
        _timer?.Dispose();
    }
}