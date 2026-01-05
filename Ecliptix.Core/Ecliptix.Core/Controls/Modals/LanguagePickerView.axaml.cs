using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public partial class LanguagePickerView : ReactiveUserControl<LanguagePickerViewModel>
{
    public LanguagePickerView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

