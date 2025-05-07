using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

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
            var mainBorder = this.FindControl<Border>("MainBorder");
    
            if (mainTextBox != null && mainBorder != null)
            {
                mainTextBox.GotFocus += (s, e) =>
                {
                    mainBorder.BorderBrush = new SolidColorBrush(Color.Parse("#6a5acd"));
                    mainBorder.BorderThickness = new Thickness(2);
                };
        
                mainTextBox.LostFocus += (s, e) =>
                {
                    mainBorder.BorderBrush = new SolidColorBrush(Colors.Gray);
                    mainBorder.BorderThickness = new Thickness(1);
                };
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        
        // private void OnLoaded(object? sender, RoutedEventArgs e)
        // {
        //     _mainTextBox = this.FindControl<TextBox>("MainTextBox");
        //     _hintStackPanel = this.FindControl<StackPanel>("HintStackPanel");
        //     _mainBorder = this.FindControl<Border>("MainBorder");
        //
        //     if (_mainBorder != null)
        //         _originalBorderBrush = _mainBorder.BorderBrush;
        //
        //     if (_mainTextBox != null)
        //     {
        //         _mainTextBox.GotFocus += MainTextBox_GotFocus;
        //         _mainTextBox.LostFocus += MainTextBox_LostFocus;
        //         _mainTextBox.PropertyChanged += MainTextBox_PropertyChanged;
        //     }
        //     UpdateHintVisibility();
        // }

        // protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        // {
        //     base.OnPropertyChanged(change);
        //     if (change.Property == TextProperty)
        //         UpdateHintVisibility();
        // }
        //
        // private void MainTextBox_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        // {
        //     if (e.Property.Name == "Text")
        //         UpdateHintVisibility();
        // }

        // private void MainTextBox_GotFocus(object? sender, GotFocusEventArgs e)
        // {
        //     if (_hintStackPanel != null)
        //     {
        //         _hintStackPanel.Opacity = 0;
        //         _hintStackPanel.IsVisible = false;  // Hide after opacity animation
        //     }
        //     if (_mainBorder != null)
        //         _mainBorder.BorderBrush = FocusBorderBrush;
        // }

        // private void MainTextBox_LostFocus(object? sender, RoutedEventArgs e)
        // {
        //     UpdateHintVisibility();
        //     if (_mainBorder != null)
        //         _mainBorder.BorderBrush = _originalBorderBrush;
        // }

        // private void UpdateHintVisibility()
        // {
        //     if (_hintStackPanel != null && _mainTextBox != null)
        //     {
        //         bool shouldShow = !string.IsNullOrEmpty(_mainTextBox.Text) == false && !_mainTextBox.IsFocused;
        //         if (shouldShow)
        //         {
        //             _hintStackPanel.IsVisible = true;  // Show before starting opacity animation
        //             _hintStackPanel.Opacity = 1;
        //         }
        //         else
        //         {
        //             _hintStackPanel.Opacity = 0;
        //             _hintStackPanel.IsVisible = false;  // Hide after opacity animation
        //         }
        //     }
        // }
    }