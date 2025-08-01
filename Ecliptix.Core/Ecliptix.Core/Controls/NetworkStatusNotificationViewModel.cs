using Ecliptix.Core.Services;
using ReactiveUI;

namespace Ecliptix.Core.Controls;

public sealed class NetworkStatusNotificationViewModel(ILocalizationService localizationService) : ReactiveObject
{
    public ILocalizationService LocalizationService { get; } = localizationService;

    private bool _isConnected = false;
    public bool IsConnected
    {
        get => _isConnected;
        set => this.RaiseAndSetIfChanged(ref _isConnected, value);
    }

    public void ChangeNetworkStatus(bool isConnected)
    {
        IsConnected = isConnected;
    }
}