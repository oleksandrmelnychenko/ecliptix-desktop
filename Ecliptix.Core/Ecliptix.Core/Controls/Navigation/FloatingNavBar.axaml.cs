using System.Collections;

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Controls.Navigation;

public partial class FloatingNavBar : UserControl
{
    public FloatingNavBar()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public static readonly StyledProperty<IEnumerable> ItemsProperty =
        AvaloniaProperty.Register<FloatingNavBar, IEnumerable>(nameof(Items));

    public IEnumerable Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    // Властивість для команди навігації
    public static readonly StyledProperty<ICommand> CommandProperty =
        AvaloniaProperty.Register<FloatingNavBar, ICommand>(nameof(Command));

    public ICommand Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}

