using System;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Features.NewContent.ViewModels;
using ReactiveUI;

namespace Ecliptix.Core.Features.NewContent.Views;

public partial class CreateWizardView : ReactiveUserControl<CreateWizardViewModel>
{
    private Border? _sizingContainer;
    private Canvas? _measureContainer;

    public CreateWizardView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            _sizingContainer = this.FindControl<Border>("SizingContainer");
            _measureContainer = this.FindControl<Canvas>("HiddenMeasureContainer");

            ViewModel!.WhenAnyValue(x => x.CurrentPage)
                .Subscribe(OnPageChanged)
                .DisposeWith(disposables);
        });
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnPageChanged(object? newPageViewModel)
    {
        if (newPageViewModel == null || _sizingContainer == null || _measureContainer == null)
        {
            return;
        }

        Control? viewToMeasure = CreateViewForViewModel(newPageViewModel);

        if (viewToMeasure == null)
        {
            return;
        }

        _measureContainer.Children.Add(viewToMeasure);

        viewToMeasure.Measure(Size.Infinity);

        Size desiredSize = viewToMeasure.DesiredSize;

        _measureContainer.Children.Remove(viewToMeasure);

        if (double.IsNaN(_sizingContainer.Width))
        {
            _sizingContainer.Width = desiredSize.Width;
            _sizingContainer.Height = desiredSize.Height;
        }
        else
        {
            _sizingContainer.Width = desiredSize.Width;
            _sizingContainer.Height = desiredSize.Height;
        }
    }

    private Control? CreateViewForViewModel(object viewModel)
    {
        return viewModel switch
        {
            CreateSelectionViewModel => new CreateSelectionView { DataContext = viewModel },
            NewChannelViewModel => new NewChannelView { DataContext = viewModel },
            NewGroupChatViewModel => new NewGroupChatView { DataContext = viewModel },
            NewPostViewModel => new NewPostView { DataContext = viewModel },
            NewContactViewModel => new NewContactView { DataContext = viewModel },
            _ => null
        };
    }
}
