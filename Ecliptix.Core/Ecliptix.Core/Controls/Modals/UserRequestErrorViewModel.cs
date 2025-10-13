using System;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Splat;

namespace Ecliptix.Core.Controls.Modals;

public class UserRequestErrorViewModel : ReactiveObject, IDisposable, IActivatableViewModel
{
    public ViewModelActivator Activator { get; } = new();
    private bool _disposed = false;
    private ILocalizationService _localizationService;
    [Reactive] public string ErrorMessage { get; set; }

    public UserRequestErrorViewModel(
        string errorMessage,
        ILocalizationService localizationService,
        Action? onComplete = null)
    {
        _localizationService = localizationService;
        ErrorMessage = errorMessage;
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
