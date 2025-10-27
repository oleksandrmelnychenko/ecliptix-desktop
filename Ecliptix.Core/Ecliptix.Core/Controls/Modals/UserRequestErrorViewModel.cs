using System;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public sealed class UserRequestErrorViewModel : ReactiveObject, IActivatableViewModel
{
    public ViewModelActivator Activator { get; } = new();

    public UserRequestErrorViewModel(string errorMessage, ILocalizationService localizationService)
    {
        _ = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? localizationService["Common.UnexpectedError"]
            : errorMessage;
    }

    public string ErrorMessage { get; }
}
