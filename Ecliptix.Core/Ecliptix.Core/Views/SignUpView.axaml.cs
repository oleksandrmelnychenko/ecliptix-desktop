using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Ecliptix.Core.Views;

public partial class SignUpView : UserControl
{
    private TextBox? _emailInput;
    private TextBlock? _emailHint;
    private Border? _emailBorder;
    
    public SignUpView()
    {
        InitializeComponent();
        
        _emailInput = this.FindControl<TextBox>("EmailInput");
        _emailHint = this.FindControl<TextBlock>("EmailHint");
        _emailBorder = this.FindControl<Border>("EmailBorder");
        
        if (_emailInput != null)
        {
            // Subscribe to GotFocus and LostFocus events
            _emailInput.GotFocus += EmailInput_GotFocus;
            _emailInput.LostFocus += EmailInput_LostFocus;
            _emailInput.PropertyChanged += EmailInput_PropertyChanged;
            
            // Initialize the hint visibility based on the initial text state
            if (_emailHint != null && !string.IsNullOrEmpty(_emailInput.Text))
            {
                _emailHint.IsVisible = false;
            }
        }
        
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void EmailInput_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == "Text" && _emailHint != null)
        {
            // Hide hint if there's text
            _emailHint.IsVisible = string.IsNullOrEmpty((string?)e.NewValue);
        }
    }

    private void EmailInput_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (_emailHint != null)
        {
            // Always hide hint when focused
            _emailHint.IsVisible = false;
        }
        
        if (_emailBorder != null)
        {
            // Change border color when focused
            _emailBorder.BorderBrush = new SolidColorBrush(Color.Parse("#6a5acd"));
        }
    }

    private void EmailInput_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (_emailInput != null && _emailHint != null)
        {
            // Show hint if there's no text when focus is lost
            _emailHint.IsVisible = string.IsNullOrEmpty(_emailInput.Text);
        }
        
        if (_emailBorder != null)
        {
            // Reset border color when focus is lost
            _emailBorder.BorderBrush = new SolidColorBrush(Color.Parse("Gray"));
        }
    }
    
}