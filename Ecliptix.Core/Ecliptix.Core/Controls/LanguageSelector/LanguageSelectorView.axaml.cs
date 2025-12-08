using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace Ecliptix.Core.Controls.LanguageSelector;

public partial class LanguageSelectorView : ReactiveUserControl<LanguageSelectorViewModel>
{
    public LanguageSelectorView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
