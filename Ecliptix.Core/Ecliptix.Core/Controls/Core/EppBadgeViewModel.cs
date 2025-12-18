using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Core;

public class EppBadgeViewModel: ReactiveObject
{
    public ILocalizationService LocalizationService { get; }

    public EppBadgeViewModel(ILocalizationService localizationService)
    {
        LocalizationService = localizationService;
    }
}
