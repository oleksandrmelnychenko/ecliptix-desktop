using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ReactiveUI;

namespace Ecliptix.Core.Controls
{
    public class NHintedTextBox : UserControl
    {
        // All the same styled properties as the original
        public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<
            NHintedTextBox,
            string
        >(nameof(Text), string.Empty);
        public static readonly StyledProperty<string> WatermarkProperty = AvaloniaProperty.Register<
            NHintedTextBox,
            string
        >(nameof(Watermark), string.Empty);
        public static readonly StyledProperty<string> HintProperty = AvaloniaProperty.Register<
            NHintedTextBox,
            string
        >(nameof(Hint), string.Empty);
        public static readonly StyledProperty<char> PasswordCharProperty =
            AvaloniaProperty.Register<NHintedTextBox, char>(nameof(PasswordChar));
        public static readonly StyledProperty<IBrush> FocusBorderBrushProperty =
            AvaloniaProperty.Register<NHintedTextBox, IBrush>(
                nameof(FocusBorderBrush),
                new SolidColorBrush(Color.Parse("#6a5acd"))
            );
        public static readonly StyledProperty<IBrush> TextForegroundProperty =
            AvaloniaProperty.Register<NHintedTextBox, IBrush>(
                nameof(TextForeground),
                new SolidColorBrush(Colors.Black)
            );
        public static readonly StyledProperty<IBrush> HintForegroundProperty =
            AvaloniaProperty.Register<NHintedTextBox, IBrush>(
                nameof(HintForeground),
                new SolidColorBrush(Colors.Gray)
            );
        public static readonly StyledProperty<string> IconSourceProperty =
            AvaloniaProperty.Register<NHintedTextBox, string>(nameof(IconSource), string.Empty);
        public static readonly StyledProperty<string> ErrorTextProperty = AvaloniaProperty.Register<
            NHintedTextBox,
            string
        >(nameof(ErrorText), string.Empty);
        public static readonly StyledProperty<double> EllipseOpacityProperty =
            AvaloniaProperty.Register<NHintedTextBox, double>(nameof(EllipseOpacity));
        public static readonly StyledProperty<bool> HasErrorProperty = AvaloniaProperty.Register<
            NHintedTextBox,
            bool
        >(nameof(HasError));
        public static readonly StyledProperty<ValidationType> ValidationTypeProperty =
            AvaloniaProperty.Register<NHintedTextBox, ValidationType>(nameof(ValidationType));
        public static readonly StyledProperty<IBrush> MainBorderBrushProperty =
            AvaloniaProperty.Register<NHintedTextBox, IBrush>(
                nameof(MainBorderBrush),
                new SolidColorBrush(Colors.LightGray)
            );
        public static readonly StyledProperty<TextWrapping> TextWrappingProperty =
            AvaloniaProperty.Register<NHintedTextBox, TextWrapping>(nameof(TextWrapping));
        public static readonly StyledProperty<int> MaxLengthProperty = AvaloniaProperty.Register<
            NHintedTextBox,
            int
        >(nameof(MaxLength), int.MaxValue);
        public static readonly StyledProperty<int> RemainingCharactersProperty =
            AvaloniaProperty.Register<NHintedTextBox, int>(
                nameof(RemainingCharacters),
                int.MaxValue
            );
        public static readonly StyledProperty<bool> ShowCharacterCounterProperty =
            AvaloniaProperty.Register<NHintedTextBox, bool>(nameof(ShowCharacterCounter));
        public static readonly StyledProperty<bool> IsNumericOnlyProperty =
            AvaloniaProperty.Register<NHintedTextBox, bool>(nameof(IsNumericOnly));
        public static new readonly StyledProperty<IBrush> BackgroundProperty =
            AvaloniaProperty.Register<NHintedTextBox, IBrush>(
                nameof(Background),
                new SolidColorBrush(Colors.White)
            );
        public static new readonly StyledProperty<double> FontSizeProperty =
            AvaloniaProperty.Register<NHintedTextBox, double>(nameof(FontSize), 14.0);
        public static new readonly StyledProperty<FontWeight> FontWeightProperty =
            AvaloniaProperty.Register<NHintedTextBox, FontWeight>(
                nameof(FontWeight),
                FontWeight.Normal
            );

        private readonly CompositeDisposable _disposables = new();
        private Border _focusBorder;
        private Border _mainBorder;
        private TextBox _mainTextBox;
        private StackPanel _counterPanel;
        private bool _isDirty;

        public event EventHandler<TextChangedEventArgs> TextChanged;

        private bool _isInitialized;

        public NHintedTextBox()
        {
            Loaded += OnControlLoaded;
        }

        private void OnControlLoaded(object? sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                Loaded -= OnControlLoaded;
                InitializeComponent();
                Initialize();
            }
        }

        // Property implementations (same as original)
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

        public bool ShowCharacterCounter
        {
            get => GetValue(ShowCharacterCounterProperty);
            set => SetValue(ShowCharacterCounterProperty, value);
        }

        public int RemainingCharacters
        {
            get => GetValue(RemainingCharactersProperty);
            set => SetValue(RemainingCharactersProperty, value);
        }

        public int MaxLength
        {
            get => GetValue(MaxLengthProperty);
            set
            {
                SetValue(MaxLengthProperty, value);
                RemainingCharacters = value - (Text?.Length ?? 0);
            }
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

        public ValidationType ValidationType
        {
            get => GetValue(ValidationTypeProperty);
            set => SetValue(ValidationTypeProperty, value);
        }

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
            set
            {
                SetValue(TextProperty, value);
                RemainingCharacters = MaxLength - (value?.Length ?? 0);
            }
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

        private void InitializeComponent()
        {
            // Create transitions
            var opacityTransition = new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(500),
            };
            Transitions = new Transitions { opacityTransition };

            // Create the main grid
            var mainGrid = new Grid();

            // Create focus border
            _focusBorder = new Border
            {
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.Parse("#6a5acd")),
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(15),
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
                CornerRadius = new CornerRadius(15),
                MinHeight = 64,
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
            };
            Grid.SetColumn(_mainTextBox, 1);

            // Create character counter
            _counterPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
            };
            Grid.SetRow(_counterPanel, 0);

            var counterText = new TextBlock { FontSize = 12, Foreground = Brushes.Gray };
            _counterPanel.Children.Add(counterText);

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
            var hintText = new TextBlock { FontSize = 13, IsHitTestVisible = false };
            hintPanel.Children.Add(hintText);

            // Create error overlay grid
            var errorGrid = new Grid { RowDefinitions = new RowDefinitions("*, *") };

            // Create error ellipse
            var errorEllipse = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(Color.Parse("#ef3a3a")),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 0, 0),
            };
            Grid.SetRow(errorEllipse, 0);

            var ellipseTransitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(200),
                },
            };
            errorEllipse.Transitions = ellipseTransitions;

            // Create error text
            var errorText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#ef3a3a")),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            Grid.SetRow(errorText, 1);

            var errorTextTransitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(200),
                },
            };
            errorText.Transitions = errorTextTransitions;

            // Assemble the control
            textInputGrid.Children.Add(_mainTextBox);
            contentGrid.Children.Add(textInputGrid);
            contentGrid.Children.Add(_counterPanel);
            contentGrid.Children.Add(hintPanel);

            errorGrid.Children.Add(errorEllipse);
            errorGrid.Children.Add(errorText);

            contentPanel.Children.Add(contentGrid);
            contentPanel.Children.Add(errorGrid);

            _mainBorder.Child = contentPanel;

            mainGrid.Children.Add(_focusBorder);
            mainGrid.Children.Add(_mainBorder);

            Content = mainGrid;

            // Set up data bindings
            SetupBindings(counterText, hintText, errorEllipse, errorText);
        }

        private void SetupBindings(
            TextBlock counterText,
            TextBlock hintText,
            Ellipse errorEllipse,
            TextBlock errorText
        )
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
            _mainTextBox.Bind(TextBox.MaxLengthProperty, this.GetObservable(MaxLengthProperty));

            _mainBorder.Bind(Border.BackgroundProperty, this.GetObservable(BackgroundProperty));
            _mainBorder.Bind(
                Border.BorderBrushProperty,
                this.GetObservable(MainBorderBrushProperty)
            );

            _counterPanel.Bind(IsVisibleProperty, this.GetObservable(ShowCharacterCounterProperty));
            counterText.Bind(
                TextBlock.TextProperty,
                this.GetObservable(RemainingCharactersProperty).Select(x => x.ToString())
            );

            hintText.Bind(TextBlock.TextProperty, this.GetObservable(HintProperty));
            hintText.Bind(TextBlock.ForegroundProperty, this.GetObservable(HintForegroundProperty));

            errorEllipse.Bind(OpacityProperty, this.GetObservable(EllipseOpacityProperty));
            errorText.Bind(TextBlock.TextProperty, this.GetObservable(ErrorTextProperty));
            errorText.Bind(OpacityProperty, this.GetObservable(EllipseOpacityProperty));
        }

        private void Initialize()
        {
            RemainingCharacters = MaxLength;
            ErrorText = string.Empty;
            HasError = false;
            EllipseOpacity = 0.0;

            // Event handlers (same as original)
            void OnTextChanged(object sender, EventArgs e)
            {
                if (!_isDirty)
                {
                    _isDirty = true;
                    return;
                }

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
                RemainingCharacters = MaxLength - input.Length;

                string validationMessage = InputValidator.Validate(input, ValidationType);
                if (!string.IsNullOrEmpty(validationMessage))
                {
                    ErrorText = validationMessage;
                    EllipseOpacity = 1.0;
                }
                else
                {
                    ErrorText = string.Empty;
                    EllipseOpacity = 0.0;
                }

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

            this.WhenAnyValue(x => x.ErrorText)
                .Subscribe(errorText =>
                {
                    HasError = !string.IsNullOrEmpty(errorText);
                    UpdateBorderState();
                })
                .DisposeWith(_disposables);

            UpdateBorderState();
        }

        private void UpdateBorderState(bool forceFocus = false)
        {
            if (HasError)
            {
                _focusBorder.BorderBrush = new SolidColorBrush(Color.Parse("#de1e31"));
                _focusBorder.Opacity = 1;
                _mainBorder.BorderBrush = Brushes.Transparent;
            }
            else if (forceFocus || _mainTextBox.IsFocused)
            {
                _focusBorder.BorderBrush = new SolidColorBrush(Color.Parse("#6a5acd"));
                _focusBorder.Opacity = 1;
                _mainBorder.BorderBrush = Brushes.Transparent;
            }
            else
            {
                _focusBorder.BorderBrush = new SolidColorBrush(Color.Parse("#6a5acd"));
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
