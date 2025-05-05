using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Ecliptix.Core.Views;

public partial class ForgotPasswordView : UserControl
{
    
    private TextBox? _phoneNumberInput;
    private TextBlock? _phoneNumberHint;
    private Border? _phoneNumberBorder;
    
    
    public ForgotPasswordView()
    {
        InitializeComponent();
        
        _phoneNumberInput = this.FindControl<TextBox>("PhoneNumberInput");
        _phoneNumberHint = this.FindControl<TextBlock>("PhoneNumberHint");
        _phoneNumberBorder = this.FindControl<Border>("PhoneNumberBorder");
        
        if (_phoneNumberInput != null)
        {
            // Subscribe to GotFocus and LostFocus events
            _phoneNumberInput.GotFocus += PhoneNumber_GotFocus;
            _phoneNumberInput.LostFocus += PhoneNumber_LostFocus;
            _phoneNumberInput.PropertyChanged += PhoneNumber_PropertyChanged;
            
            // Initialize the hint visibility based on the initial text state
            if (_phoneNumberHint != null && !string.IsNullOrEmpty(_phoneNumberInput.Text))
            {
                _phoneNumberHint.IsVisible = false;
            }
        }
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    private void PhoneNumber_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == "Text" && _phoneNumberHint != null)
        {
            // Hide hint if there's text
            _phoneNumberHint.IsVisible = string.IsNullOrEmpty((string?)e.NewValue);
        }
    }

    private void PhoneNumber_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (_phoneNumberHint != null)
        {
            // Always hide hint when focused
            _phoneNumberHint.IsVisible = false;
        }
        
        if (_phoneNumberBorder != null)
        {
            // Change border color when focused
            _phoneNumberBorder.BorderBrush = new SolidColorBrush(Color.Parse("#6a5acd"));
        }
    }

    private void PhoneNumber_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (_phoneNumberInput != null && _phoneNumberHint != null)
        {
            // Show hint if there's no text when focus is lost
            _phoneNumberHint.IsVisible = string.IsNullOrEmpty(_phoneNumberInput.Text);
        }
        
        if (_phoneNumberBorder != null)
        {
            // Reset border color when focus is lost
            _phoneNumberBorder.BorderBrush = new SolidColorBrush(Color.Parse("Gray"));
        }
    }
}