using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public partial class CountryCodeView : ReactiveUserControl<CountryCodeViewModel>
{
    public CountryCodeView()
    {
        InitializeComponent();
    }
}

