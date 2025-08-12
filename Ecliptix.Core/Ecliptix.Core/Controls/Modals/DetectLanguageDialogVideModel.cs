using Ecliptix.Core.Services;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public class DetectLanguageDialogVideModel(ILocalizationService localizationService) : ReactiveObject
{
    public ILocalizationService LocalizationService { get; } = localizationService;
}