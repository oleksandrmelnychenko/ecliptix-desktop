using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Ecliptix.Core.ViewModels.Memberships;
using ReactiveUI;

namespace Ecliptix.Core.Controls;

public partial class HintedTextBox : UserControl
    {
        // Dependency properties declarations remain unchanged
        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<HintedTextBox, string>(nameof(Text), string.Empty);
        public static readonly StyledProperty<string> WatermarkProperty =
            AvaloniaProperty.Register<HintedTextBox, string>(nameof(Watermark), string.Empty);
        public static readonly StyledProperty<string> HintProperty =
            AvaloniaProperty.Register<HintedTextBox, string>(nameof(Hint), string.Empty);
        public static readonly StyledProperty<char> PasswordCharProperty =
            AvaloniaProperty.Register<HintedTextBox, char>(nameof(PasswordChar), '\0');
        public static readonly StyledProperty<IBrush> FocusBorderBrushProperty =
            AvaloniaProperty.Register<HintedTextBox, IBrush>(nameof(FocusBorderBrush),
                new SolidColorBrush(Color.Parse("#6a5acd")));
        public static readonly StyledProperty<IBrush> TextForegroundProperty =
            AvaloniaProperty.Register<HintedTextBox, IBrush>(
                nameof(TextForeground),
                new SolidColorBrush(Colors.Black));
        public static readonly StyledProperty<IBrush> HintForegroundProperty =
            AvaloniaProperty.Register<HintedTextBox, IBrush>(
                nameof(HintForeground),
                new SolidColorBrush(Colors.Gray));
        public static readonly StyledProperty<string> IconSourceProperty =
            AvaloniaProperty.Register<HintedTextBox, string>(nameof(IconSource), string.Empty);
        public static readonly StyledProperty<string> ErrorTextProperty =
            AvaloniaProperty.Register<HintedTextBox, string>(nameof(ErrorText), string.Empty);
        public static readonly StyledProperty<double> EllipseOpacityProperty =
            AvaloniaProperty.Register<HintedTextBox, double>(nameof(EllipseOpacity), 0.0);
        public static readonly StyledProperty<bool> HasErrorProperty =
            AvaloniaProperty.Register<HintedTextBox, bool>(nameof(HasError), false);

        public bool HasError
        {
            get => GetValue(HasErrorProperty);
            set => SetValue(HasErrorProperty, value);
        }
        
        public double EllipseOpacity
        {
            get => GetValue(EllipseOpacityProperty);
            set => SetValue(EllipseOpacityProperty, value);
        }
        
        public string IconSource
        {
            get => GetValue(IconSourceProperty);
            set => SetValue(IconSourceProperty, value);
        }
        public IBrush TextForeground
        {
            get => GetValue(TextForegroundProperty);
            set => SetValue(TextForegroundProperty, value);
        }
        public IBrush HintForeground
        {
            get => GetValue(HintForegroundProperty);
            set => SetValue(HintForegroundProperty, value);
        }
        public string Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
        public string Watermark
        {
            get => GetValue(WatermarkProperty);
            set => SetValue(WatermarkProperty, value);
        }
        public string Hint
        {
            get => GetValue(HintProperty);
            set => SetValue(HintProperty, value);
        }
        public char PasswordChar
        {
            get => GetValue(PasswordCharProperty);
            set => SetValue(PasswordCharProperty, value);
        }
        public IBrush FocusBorderBrush
        {
            get => GetValue(FocusBorderBrushProperty);
            set => SetValue(FocusBorderBrushProperty, value);
        }

        public string ErrorText
        {
            get => GetValue(ErrorTextProperty);
            set => SetValue(ErrorTextProperty, value);
        }
      
        // Private fields for controls
        private TextBox? _mainTextBox;
        private StackPanel? _hintStackPanel;
        private Border? _mainBorder;
        private IBrush _originalBorderBrush;

        public HintedTextBox()
        {
            InitializeComponent();
            var mainTextBox = this.FindControl<TextBox>("MainTextBox");
            var focusBorder = this.FindControl<Border>("FocusBorder");

            if (mainTextBox != null && focusBorder != null)
            {
                this.WhenAnyValue(x => x.ErrorText)
                    .Subscribe(errorText =>
                    {
                        HasError = !string.IsNullOrEmpty(errorText);
                        UpdateBorderState(mainTextBox, focusBorder);
                    });

                mainTextBox.GotFocus += (s, e) =>
                {
                    UpdateBorderState(mainTextBox, focusBorder, true);
                    if (DataContext is SignInViewModel vm)
                    {
                        if (Name == "MobileTextBox")
                            vm.MobileFieldTouched = true;
                        else if (Name == "PasswordTextBox")
                            vm.PasswordFieldTouched = true;
                    }
                };

                mainTextBox.LostFocus += (s, e) =>
                {
                    UpdateBorderState(mainTextBox, focusBorder);
                };
            }
        }

        private void UpdateBorderState(TextBox mainTextBox, Border focusBorder, bool forceFocus = false)
        {
            var mainBorder = this.FindControl<Border>("MainBorder");
    
            if (HasError)
            {
                focusBorder.BorderBrush = new SolidColorBrush(Color.Parse("#de1e31"));
                focusBorder.Opacity = 1;
                mainBorder.BorderBrush = new SolidColorBrush(Colors.Transparent);
            }
            else if (forceFocus || mainTextBox.IsFocused)
            {
                focusBorder.BorderBrush = new SolidColorBrush(Color.Parse("#6a5acd"));
                focusBorder.Opacity = 1;
                mainBorder.BorderBrush = new SolidColorBrush(Colors.Transparent);
            }
            else
            {
                focusBorder.BorderBrush = new SolidColorBrush(Color.Parse("#6a5acd"));
                focusBorder.Opacity = 0;
                mainBorder.BorderBrush = new SolidColorBrush(Color.Parse("#808080")); // Gray
            }
        }
        
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        
       
    }