using Ecliptix.Core.Services;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public class LanguageDetectionViewModel : ReactiveObject
{
    public ILocalizationService LocalizationService { get; }

    public LanguageDetectionViewModel(ILocalizationService localizationService)
    {
        LocalizationService = localizationService;
    }
}