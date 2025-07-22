using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Services;

namespace Ecliptix.Core.Controls.LanguageSwitcher;

public partial class LanguageSwitcherView : ReactiveUserControl<LanguageSwitcherViewModel>
{
    public LanguageSwitcherView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public LanguageSwitcherView(ILocalizationService localizationService) : this()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = new LanguageSwitcherViewModel(localizationService);
    }
}