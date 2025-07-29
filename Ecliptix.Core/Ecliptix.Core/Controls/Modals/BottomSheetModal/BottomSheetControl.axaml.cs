using System;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Avalonia.Styling;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal
{
    public static class DefaultBottomSheetVariables
    {
        public const double MinHeight = 200.0;
        public const double MaxHeight = 600.0;
        public const double DefaultAppearVerticalOffset = 0.0;
        public const double DefaultDisappearVerticalOffset = 0.0;
        public const double DefaultOpacity = 0.5;
        public const double DefaultToOpacity = 1.0;
        public const double DefaultAnimationDuration = 300.0; 
    }
    
    public partial class BottomSheetControl : ReactiveUserControl<BottomSheetViewModel>
    {
        private readonly TimeSpan _animationDuration = TimeSpan.FromMilliseconds(DefaultBottomSheetVariables.DefaultAnimationDuration);
        private Animation? _showAnimation;
        private Animation? _hideAnimation;
        private double _sheetHeight;

        public BottomSheetControl()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            
            this.IsVisible = false;
            
            this.WhenActivated(disposables =>
            {
                SetupContentObservables(disposables);
                SetupVisibilityObservable(disposables);
            });
        }
        
        public static readonly StyledProperty<double> AppearVerticalOffsetProperty =
            AvaloniaProperty.Register<BottomSheetControl, double>(nameof(AppearVerticalOffset), DefaultBottomSheetVariables.DefaultAppearVerticalOffset);

        public static readonly StyledProperty<double> DisappearVerticalOffsetProperty =
            AvaloniaProperty.Register<BottomSheetControl, double>(nameof(DisappearVerticalOffset), DefaultBottomSheetVariables.DefaultDisappearVerticalOffset);

        public new static readonly StyledProperty<double> MinHeightProperty =
            AvaloniaProperty.Register<BottomSheetControl, double>(nameof(MinHeight), DefaultBottomSheetVariables.MinHeight);

        public new static readonly StyledProperty<double> MaxHeightProperty =
            AvaloniaProperty.Register<BottomSheetControl, double>(nameof(MaxHeight), DefaultBottomSheetVariables.MaxHeight);

        public new double MinHeight
        {
            get => GetValue(MinHeightProperty);
            set => SetValue(MinHeightProperty, value);
        }

        public new double MaxHeight
        {
            get => GetValue(MaxHeightProperty);
            set => SetValue(MaxHeightProperty, value);
        }
        public double AppearVerticalOffset
        {
            get => GetValue(AppearVerticalOffsetProperty);
            set => SetValue(AppearVerticalOffsetProperty, value);
        }

        public double DisappearVerticalOffset
        {
            get => GetValue(DisappearVerticalOffsetProperty);
            set => SetValue(DisappearVerticalOffsetProperty, value);
        }

        
        private void SetupContentObservables(CompositeDisposable disposables)
        {
            Border? sheetBorder = this.FindControl<Border>("SheetBorder");
            ItemsControl? contentItems = this.FindControl<ItemsControl>("ContentItems");

            if (sheetBorder != null && contentItems != null)
            {
                contentItems.GetObservable(BoundsProperty)
                    .Subscribe(_ =>
                    {
                        UpdateSheetHeight(sheetBorder);
                        CreateAnimations();
                    })
                    .DisposeWith(disposables);

                contentItems.GetObservable(MarginProperty)
                    .Subscribe(_ =>
                    {
                        UpdateSheetHeight(sheetBorder);
                        CreateAnimations();
                    })
                    .DisposeWith(disposables);

                UpdateSheetHeight(sheetBorder);
                CreateAnimations();
            }
        }

        private void SetupVisibilityObservable(CompositeDisposable disposables)
        {
            Border? sheetBorder = this.FindControl<Border>("SheetBorder");

            this.WhenAnyValue(x => x.ViewModel!.IsVisible)
                .Subscribe(async isVisible =>
                {
                    if (sheetBorder == null) return;

                    SetupViewForAnimation(sheetBorder);

                    if (isVisible)
                    {
                        this.IsVisible = true;
                        await _showAnimation!.RunAsync(sheetBorder, CancellationToken.None);
                    }
                    else
                    {
                        await _hideAnimation!.RunAsync(sheetBorder, CancellationToken.None);
                        await Task.Delay(_animationDuration);
                        this.IsVisible = false;
                    }
                })
                .DisposeWith(disposables);
        }
        

        private void UpdateSheetHeight(Border sheetBorder)
        {
            ItemsControl? contentItems = this.FindControl<ItemsControl>("ContentItems");
            if (contentItems == null) return;

            double verticalMargin = contentItems.Margin.Top + contentItems.Margin.Bottom;
            double contentHeight = contentItems.DesiredSize.Height + verticalMargin;
            _sheetHeight = Math.Clamp(contentHeight, MinHeight, MaxHeight);
            sheetBorder.Height = _sheetHeight;
        }

        private void CreateAnimations()
        {
            double hiddenPosition = _sheetHeight + DisappearVerticalOffset;

            _showAnimation = new Animation
            {
                Duration = _animationDuration,
                Easing = new CubicEaseInOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0.0),
                        Setters =
                        {
                            new Setter(TranslateTransform.YProperty, hiddenPosition),
                            new Setter(Visual.OpacityProperty, DefaultBottomSheetVariables.DefaultOpacity)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters =
                        {
                            new Setter(TranslateTransform.YProperty, AppearVerticalOffset),
                            new Setter(Visual.OpacityProperty, DefaultBottomSheetVariables.DefaultToOpacity)
                        }
                    }
                }
            };

            _hideAnimation = new Animation
            {
                Duration = _animationDuration,
                Easing = new CubicEaseInOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0.0),
                        Setters =
                        {
                            new Setter(TranslateTransform.YProperty, AppearVerticalOffset),
                            new Setter(Visual.OpacityProperty, DefaultBottomSheetVariables.DefaultToOpacity)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters =
                        {
                            new Setter(TranslateTransform.YProperty, hiddenPosition),
                            new Setter(Visual.OpacityProperty, DefaultBottomSheetVariables.DefaultOpacity)
                        }
                    }
                }
            };
        }

        private void SetupViewForAnimation(Visual view)
        {
            view.RenderTransformOrigin = RelativePoint.TopLeft;
            TranslateTransform translateTransform = EnsureTransform<TranslateTransform>(view);
            translateTransform.Y = _sheetHeight + DisappearVerticalOffset;
            view.Opacity = DefaultBottomSheetVariables.DefaultOpacity;
            view.IsVisible = true;
        }

        private static T EnsureTransform<T>(Visual visual) where T : Transform, new()
        {
            TransformGroup? transformGroup = visual.RenderTransform as TransformGroup;

            if (transformGroup == null)
            {
                transformGroup = new TransformGroup();
                if (visual.RenderTransform is Transform existingTransform)
                    transformGroup.Children.Add(existingTransform);
                visual.RenderTransform = transformGroup;
            }

            foreach (Transform? child in transformGroup.Children)
            {
                if (child is T existing) return existing;
            }

            T newTransform = new T();
            transformGroup.Children.Add(newTransform);
            return newTransform;
        }
    }
}