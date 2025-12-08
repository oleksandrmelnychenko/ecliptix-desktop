using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace Ecliptix.Core.Controls.Core;

public partial class LanguageCycleButtonView : ReactiveUserControl<LanguageCycleButtonViewModel>
{
    public LanguageCycleButtonView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
