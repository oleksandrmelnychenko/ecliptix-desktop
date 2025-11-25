using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Ecliptix.Core.Shared.Transitions;

public partial class AnimatingContentControl : UserControl
{
    private const int ANIMATION_STATE_BUFFER_MS = 5;

    private static readonly TimeSpan DefaultAnimationDuration = TimeSpan.FromMilliseconds(125);

    public AnimatingContentControl()
    {
        InitializeComponent();
    }

    private CancellationTokenSource? _currentTransition;
    private ContentPresenter? _lastPresenter;
    private ContentPresenter? _presenter2;
    private bool _isFirstFull;
    private bool _shouldAnimate;

    public static readonly StyledProperty<bool> IsAnimatingProperty =
        AvaloniaProperty.Register<AnimatingContentControl, bool>(nameof(IsAnimating));

    public new bool IsAnimating
    {
        get => GetValue(IsAnimatingProperty);
        set => SetValue(IsAnimatingProperty, value);
    }

    public static readonly StyledProperty<IPageTransition?> PageTransitionProperty =
        AvaloniaProperty.Register<AnimatingContentControl, IPageTransition?>(
            nameof(PageTransition),
            defaultValue: new CrossFade(DefaultAnimationDuration));

    public IPageTransition? PageTransition
    {
        get => GetValue(PageTransitionProperty);
        set => SetValue(PageTransitionProperty, value);
    }

    public static readonly StyledProperty<bool> IsTransitionReversedProperty =
        AvaloniaProperty.Register<AnimatingContentControl, bool>(
            nameof(IsTransitionReversed),
            defaultValue: false);

    public bool IsTransitionReversed
    {
        get => GetValue(IsTransitionReversedProperty);
        set => SetValue(IsTransitionReversedProperty, value);
    }

    public static readonly RoutedEvent<TransitionCompletedEventArgs> TransitionCompletedEvent =
        RoutedEvent.Register<AnimatingContentControl, TransitionCompletedEventArgs>(
            nameof(TransitionCompleted),
            RoutingStrategies.Direct);

    public event EventHandler<TransitionCompletedEventArgs> TransitionCompleted
    {
        add => AddHandler(TransitionCompletedEvent, value);
        remove => RemoveHandler(TransitionCompletedEvent, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _presenter2 = e.NameScope.Find<ContentPresenter>("PART_ContentPresenter2");

        UpdateContent(false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size result = base.ArrangeOverride(finalSize);

        if (_shouldAnimate)
        {
            _currentTransition?.Cancel();

            if (_presenter2 is not null &&
                Presenter is { } presenter &&
                PageTransition is { } transition)
            {
                _shouldAnimate = false;

                CancellationTokenSource cancel = new();
                _currentTransition = cancel;

                ContentPresenter? from = _isFirstFull ? _presenter2 : presenter;
                ContentPresenter? to = _isFirstFull ? presenter : _presenter2;
                object? fromContent = from.Content;
                object? toContent = to.Content;

                transition.Start(from, to, !IsTransitionReversed, cancel.Token).ContinueWith(task =>
                {
                    if (!cancel.IsCancellationRequested)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            OnTransitionCompleted(new TransitionCompletedEventArgs(
                                fromContent, toContent, task.Status == TaskStatus.RanToCompletion));

                            HideOldPresenter();

                            DispatcherTimer timer = new()
                            {
                                Interval = TimeSpan.FromMilliseconds(ANIMATION_STATE_BUFFER_MS)
                            };

                            void Handler(object? s, EventArgs e)
                            {
                                timer.Tick -= Handler;
                                timer.Stop();
                                if (!cancel.IsCancellationRequested)
                                {
                                    IsAnimating = false;
                                }
                            }

                            timer.Tick += Handler;

                            timer.Start();
                        });
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else
            {
                _shouldAnimate = false;
                IsAnimating = false;
            }
        }

        return result;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == ContentProperty)
        {
            UpdateContent(true);
            return;
        }

        base.OnPropertyChanged(change);
    }

    private void UpdateContent(bool withTransition)
    {
        if (VisualRoot is null || _presenter2 is null || Presenter is null)
        {
            return;
        }

        ContentPresenter? currentPresenter = _isFirstFull ? _presenter2 : Presenter;
        object? fromContent = _lastPresenter?.Content;
        object? toContent = Content;

        if (_lastPresenter != null &&
            _lastPresenter != currentPresenter &&
            _lastPresenter.Content == toContent)
        {
            _lastPresenter.Content = null;
        }

        currentPresenter.Content = toContent;
        currentPresenter.IsVisible = true;
        _lastPresenter = currentPresenter;

        _isFirstFull = !_isFirstFull;

        if (PageTransition is not null && withTransition)
        {
            _shouldAnimate = true;
            IsAnimating = true;
            InvalidateArrange();
        }
        else
        {
            HideOldPresenter();
            OnTransitionCompleted(new TransitionCompletedEventArgs(fromContent, toContent, false));
            IsAnimating = false;
        }
    }

    private void HideOldPresenter()
    {
        ContentPresenter? oldPresenter = _isFirstFull ? _presenter2 : Presenter;
        if (oldPresenter is not null)
        {
            oldPresenter.Content = null;
            oldPresenter.IsVisible = false;
        }
    }

    private void OnTransitionCompleted(TransitionCompletedEventArgs e)
        => RaiseEvent(e);
}

