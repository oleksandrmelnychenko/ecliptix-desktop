using System;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Avalonia.Media;
using Ecliptix.Core.ViewModels.Memberships;
using ReactiveUI;

namespace Ecliptix.Core.Views.Memberships;

public partial class WelcomeView : ReactiveUserControl<WelcomeViewModel>
{
    private ItemsControl _indicatorsControl;

    public WelcomeView()
    {
        AvaloniaXamlLoader.Load(this);
        _indicatorsControl = this.FindControl<ItemsControl>("SlideIndicators");

        this.WhenActivated(disposables =>
        {
            if (ViewModel != null)
            {
                this.WhenAnyValue(x => x.ViewModel.SelectedSlideIndex)
                    .Subscribe(UpdateSlideIndicators)
                    .DisposeWith(disposables);
            }
        });
    }

    private void UpdateSlideIndicators(int selectedIndex)
    {
        if (_indicatorsControl == null || _indicatorsControl.ItemCount == 0)
            return;

        for (int i = 0; i < _indicatorsControl.ItemCount; i++)
        {
            if (_indicatorsControl.ContainerFromIndex(i) is ContentPresenter presenter &&
                presenter.Child is Border indicator)
            {
                indicator.Background = i == selectedIndex
                    ? new SolidColorBrush(Color.Parse("#272320"))
                    : new SolidColorBrush(Color.Parse("#D0D0D0"));
            }
        }
    }

    private void OnIndicatorTapped(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Border tappedIndicator && ViewModel != null)
        {
            // Find the index of the tapped indicator
            for (int i = 0; i < _indicatorsControl.ItemCount; i++)
            {
                if (_indicatorsControl.ContainerFromIndex(i) is ContentPresenter presenter &&
                    presenter.Child == tappedIndicator)
                {
                    // Update the selected index in the ViewModel
                    ViewModel.SelectedSlideIndex = i;

                    // Also scroll to show that slide in the carousel
                    if (this.FindControl<ScrollViewer>("CarouselScrollViewer") is ScrollViewer scrollViewer)
                    {
                        // Assuming each slide has the same width (240) plus spacing (16)
                        double slideWidth = 256; // 240 width + 16 spacing
                        scrollViewer.Offset = new Vector(i * slideWidth, 0);
                    }

                    break;
                }
            }
        }
    }
}