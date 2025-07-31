using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;

namespace Ecliptix.Core.Controls.LanguageSwitcher;

public partial class LanguageSwitcherView : ReactiveUserControl<LanguageSwitcherViewModel>
{
    public LanguageSwitcherView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public LanguageSwitcherView(ILocalizationService localizationService,
        ISecureStorageProvider secureStorageProvider, IRpcMetaDataProvider rpcMetaDataProvider) : this()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = new LanguageSwitcherViewModel(localizationService, secureStorageProvider, rpcMetaDataProvider);
    }
}