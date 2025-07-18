using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ReactiveUI;

namespace Ecliptix.Core.Controls
{
    public class PNRHintedTextBox : UserControl
    {
        // Properties remain the same
        public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<
            PNRHintedTextBox,
            string
        >(nameof(Text), string.Empty);
        public static readonly StyledProperty<string> WatermarkProperty = AvaloniaProperty.Register<
            PNRHintedTextBox,
            string
        >(nameof(Watermark), string.Empty);
        public static readonly StyledProperty<string> HintProperty = AvaloniaProperty.Register<
            PNRHintedTextBox,
            string
        >(nameof(Hint), string.Empty);
        public static readonly StyledProperty<char> PasswordCharProperty =
            AvaloniaProperty.Register<PNRHintedTextBox, char>(nameof(PasswordChar));
        public static readonly StyledProperty<IBrush> FocusBorderBrushProperty =
            AvaloniaProperty.Register<PNRHintedTextBox, IBrush>(
                nameof(FocusBorderBrush),
                new SolidColorBrush(Color.Parse("#6a5acd"))
            );
        public static readonly StyledProperty<IBrush> TextForegroundProperty =
            AvaloniaProperty.Register<PNRHintedTextBox, IBrush>(
                nameof(TextForeground),
                new SolidColorBrush(Colors.Black)
            );
        public static readonly StyledProperty<IBrush> HintForegroundProperty =
            AvaloniaProperty.Register<PNRHintedTextBox, IBrush>(
                nameof(HintForeground),
                new SolidColorBrush(Colors.Gray)
            );
        public static readonly StyledProperty<string> IconSourceProperty =
            AvaloniaProperty.Register<PNRHintedTextBox, string>(nameof(IconSource), string.Empty);
        public static readonly StyledProperty<IBrush> MainBorderBrushProperty =
            AvaloniaProperty.Register<PNRHintedTextBox, IBrush>(
                nameof(MainBorderBrush),
                new SolidColorBrush(Colors.LightGray)
            );
        public static readonly StyledProperty<TextWrapping> TextWrappingProperty =
            AvaloniaProperty.Register<PNRHintedTextBox, TextWrapping>(nameof(TextWrapping));
        public static readonly StyledProperty<bool> IsNumericOnlyProperty =
            AvaloniaProperty.Register<PNRHintedTextBox, bool>(nameof(IsNumericOnly));
        public static new readonly StyledProperty<IBrush> BackgroundProperty =
            AvaloniaProperty.Register<PNRHintedTextBox, IBrush>(
                nameof(Background),
                new SolidColorBrush(Colors.White)
            );
        public static readonly StyledProperty<double> CornerRadiusProperty =
            AvaloniaProperty.Register<PNRHintedTextBox, double>(nameof(CornerRadius), 15);
        public static new readonly StyledProperty<double> FontSizeProperty =
            AvaloniaProperty.Register<PNRHintedTextBox, double>(nameof(FontSize), 14.0);
        public static new readonly StyledProperty<FontWeight> FontWeightProperty =
            AvaloniaProperty.Register<PNRHintedTextBox, FontWeight>(
                nameof(FontWeight),
                FontWeight.Normal
            );

        private readonly CompositeDisposable _disposables = new();
        private Border _focusBorder;
        private Border _mainBorder;
        private TextBox _mainTextBox;
        private bool _isInitialized = false;

        public event EventHandler<TextChangedEventArgs> TextChanged;

        public PNRHintedTextBox()
        {
            // Show placeholder immediately that matches our final dimensions
            Content = new Border
            {
                MinHeight = 64,
                Background = Background,
                CornerRadius = new CornerRadius(CornerRadius),
                BorderThickness = new Thickness(1),
                BorderBrush = MainBorderBrush,
            };

            // Initialize on Loaded instead of background thread
            Loaded += OnControlLoaded;
        }

        // Add corner radius property
        public double CornerRadius
        {
            get => GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        // Property implementations
        public new FontWeight FontWeight
        {
            get => GetValue(FontWeightProperty);
            set => SetValue(FontWeightProperty, value);
        }

        public new double FontSize
        {
            get => GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public new IBrush Background
        {
            get => GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        public bool IsNumericOnly
        {
            get => GetValue(IsNumericOnlyProperty);
            set => SetValue(IsNumericOnlyProperty, value);
        }

        public TextWrapping TextWrapping
        {
            get => GetValue(TextWrappingProperty);
            set => SetValue(TextWrappingProperty, value);
        }

        public IBrush MainBorderBrush
        {
            get => GetValue(MainBorderBrushProperty);
            set => SetValue(MainBorderBrushProperty, value);
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

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                // Unregister the event to avoid multiple initializations
                Loaded -= OnControlLoaded;

                // Create the actual control
                Content = InitializeComponent();
                Initialize();
            }
        }

        private Grid InitializeComponent()
        {
            // Create the main grid
            var mainGrid = new Grid();

            // Create focus border
            _focusBorder = new Border
            {
                BorderThickness = new Thickness(2),
                BorderBrush = FocusBorderBrush,
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(CornerRadius),
                Opacity = 0,
            };

            var focusTransitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Border.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(200),
                },
                new BrushTransition
                {
                    Property = Border.BorderBrushProperty,
                    Duration = TimeSpan.FromMilliseconds(200),
                },
            };
            _focusBorder.Transitions = focusTransitions;

            // Create main border
            _mainBorder = new Border
            {
                Margin = new Thickness(1),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(CornerRadius),
                MinHeight = 64,
                Background = Background,
                BorderBrush = MainBorderBrush,
            };

            var mainBorderTransitions = new Transitions
            {
                new BrushTransition
                {
                    Property = Border.BorderBrushProperty,
                    Duration = TimeSpan.FromMilliseconds(200),
                },
            };
            _mainBorder.Transitions = mainBorderTransitions;

            // Create content panel
            var contentPanel = new Panel { Margin = new Thickness(12, 15, 8, 15) };

            // Create main content grid
            var contentGrid = new Grid
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                RowDefinitions = new RowDefinitions("*,Auto"),
            };

            // Create text input grid
            var textInputGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Grid.SetRow(textInputGrid, 0);

            // Create main text box
            _mainTextBox = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
                MinHeight = 32,
                MinWidth = 32,
                FontSize = FontSize,
                FontWeight = FontWeight,
                Foreground = TextForeground,
                Watermark = Watermark,
                Text = Text,
                PasswordChar = PasswordChar,
                TextWrapping = TextWrapping,
            };
            Grid.SetColumn(_mainTextBox, 1);

            // Create hint panel
            var hintPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(2, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(hintPanel, 1);

            var hintTransitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(300),
                },
            };
            hintPanel.Transitions = hintTransitions;

            // Create hint text
            var hintText = new TextBlock
            {
                FontSize = 13,
                IsHitTestVisible = false,
                Text = Hint,
                Foreground = HintForeground,
            };
            hintPanel.Children.Add(hintText);

            // Assemble the control
            textInputGrid.Children.Add(_mainTextBox);
            contentGrid.Children.Add(textInputGrid);
            contentGrid.Children.Add(hintPanel);

            contentPanel.Children.Add(contentGrid);
            _mainBorder.Child = contentPanel;

            mainGrid.Children.Add(_focusBorder);
            mainGrid.Children.Add(_mainBorder);

            SetupBindings(hintText);

            return mainGrid;
        }

        private void SetupBindings(TextBlock hintText)
        {
            // Bind properties
            _mainTextBox.Bind(TextBox.WatermarkProperty, this.GetObservable(WatermarkProperty));
            _mainTextBox.Bind(TextBox.TextProperty, this.GetObservable(TextProperty));
            _mainTextBox.Bind(
                TextBox.PasswordCharProperty,
                this.GetObservable(PasswordCharProperty)
            );
            _mainTextBox.Bind(
                TextBox.ForegroundProperty,
                this.GetObservable(TextForegroundProperty)
            );
            _mainTextBox.Bind(TextBox.FontSizeProperty, this.GetObservable(FontSizeProperty));
            _mainTextBox.Bind(TextBox.FontWeightProperty, this.GetObservable(FontWeightProperty));
            _mainTextBox.Bind(
                TextBox.TextWrappingProperty,
                this.GetObservable(TextWrappingProperty)
            );

            _mainBorder.Bind(Border.BackgroundProperty, this.GetObservable(BackgroundProperty));
            _mainBorder.Bind(
                Border.BorderBrushProperty,
                this.GetObservable(MainBorderBrushProperty)
            );
            _mainBorder.Bind(
                Border.CornerRadiusProperty,
                this.GetObservable(CornerRadiusProperty).Select(radius => new CornerRadius(radius))
            );

            _focusBorder.Bind(
                Border.CornerRadiusProperty,
                this.GetObservable(CornerRadiusProperty).Select(radius => new CornerRadius(radius))
            );

            hintText.Bind(TextBlock.TextProperty, this.GetObservable(HintProperty));
            hintText.Bind(TextBlock.ForegroundProperty, this.GetObservable(HintForegroundProperty));
        }

        private void Initialize()
        {
            // Simplified event handlers without validation
            void OnTextChanged(object sender, EventArgs e)
            {
                string input = _mainTextBox.Text ?? string.Empty;

                if (IsNumericOnly)
                {
                    string numeric = string.Concat(input.Where(char.IsDigit));
                    if (numeric != input)
                    {
                        _mainTextBox.Text = numeric;
                        input = numeric;
                    }
                }

                SetValue(TextProperty, input);
                UpdateBorderState();
                TextChanged?.Invoke(this, new TextChangedEventArgs(TextBox.TextChangedEvent));
            }

            void OnGotFocus(object sender, GotFocusEventArgs e)
            {
                UpdateBorderState(true);
            }

            void OnLostFocus(object sender, RoutedEventArgs e)
            {
                UpdateBorderState();
            }

            _mainTextBox.TextChanged += OnTextChanged;
            _mainTextBox.GotFocus += OnGotFocus;
            _mainTextBox.LostFocus += OnLostFocus;

            _disposables.Add(
                Disposable.Create(() =>
                {
                    _mainTextBox.TextChanged -= OnTextChanged;
                    _mainTextBox.GotFocus -= OnGotFocus;
                    _mainTextBox.LostFocus -= OnLostFocus;
                })
            );

            UpdateBorderState();
        }

        private void UpdateBorderState(bool forceFocus = false)
        {
            if (forceFocus || _mainTextBox.IsFocused)
            {
                _focusBorder.BorderBrush = FocusBorderBrush;
                _focusBorder.Opacity = 1;
                _mainBorder.BorderBrush = Brushes.Transparent;
            }
            else
            {
                _focusBorder.BorderBrush = FocusBorderBrush;
                _focusBorder.Opacity = 0;
                _mainBorder.BorderBrush = MainBorderBrush;
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _disposables.Dispose();
        }
    }
}
