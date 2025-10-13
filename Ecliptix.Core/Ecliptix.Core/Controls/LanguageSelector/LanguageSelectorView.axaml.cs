using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Services.Abstractions.Core;

namespace Ecliptix.Core.Controls.LanguageSelector;

public partial class LanguageSelectorView : ReactiveUserControl<LanguageSelectorViewModel>
{
    public LanguageSelectorView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public LanguageSelectorView(ILocalizationService localizationService,
        IApplicationSecureStorageProvider applicationSecureStorageProvider, IRpcMetaDataProvider rpcMetaDataProvider) : this()
    {
        DataContext = new LanguageSelectorViewModel(localizationService, applicationSecureStorageProvider, rpcMetaDataProvider);
    }
}
