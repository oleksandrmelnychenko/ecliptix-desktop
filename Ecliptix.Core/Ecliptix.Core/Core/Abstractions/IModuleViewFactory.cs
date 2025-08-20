using System;
using Avalonia.Controls;

namespace Ecliptix.Core.Core.Abstractions;

public interface IModuleViewFactory
{
    void RegisterView<TViewModel, TView>()
        where TViewModel : class
        where TView : Control, new();

    Control? CreateView(Type viewModelType);

    Control? CreateView<TViewModel>(TViewModel viewModel) where TViewModel : class;

    bool HasView(Type viewModelType);

    int RegisteredViewCount { get; }
}