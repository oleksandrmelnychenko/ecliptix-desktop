using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Services;

namespace Ecliptix.Core.Controls.Modals;

public partial class LanguageDetectionModal : UserControl
{
    public LanguageDetectionModal(ILocalizationService localizationService)
    {
        InitializeComponent();
        DataContext = new LanguageDetectionViewModel(localizationService);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}