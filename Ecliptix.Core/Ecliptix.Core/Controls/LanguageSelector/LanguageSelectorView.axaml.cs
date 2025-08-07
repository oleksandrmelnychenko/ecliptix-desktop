using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Network.Contracts.Transport;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;

namespace Ecliptix.Core.Controls.LanguageSelector;

public partial class LanguageSelectorView : ReactiveUserControl<LanguageSelectorViewModel>
{
    public LanguageSelectorView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public LanguageSelectorView(ILocalizationService localizationService,
        ISecureStorageProvider secureStorageProvider, IRpcMetaDataProvider rpcMetaDataProvider) : this()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = new LanguageSelectorViewModel(localizationService, secureStorageProvider, rpcMetaDataProvider);
    }
}