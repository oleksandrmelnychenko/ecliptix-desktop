using System;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Core.Localization;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public sealed class UserRequestErrorViewModel : ReactiveObject, IActivatableViewModel
{
    public ViewModelActivator Activator { get; } = new();

    public UserRequestErrorViewModel(string errorMessage, ILocalizationService localizationService)
    {
        _ = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? localizationService[LocalizationKeys.Common.UNEXPECTED_ERROR]
            : errorMessage;
    }

    public string ErrorMessage { get; }
}
