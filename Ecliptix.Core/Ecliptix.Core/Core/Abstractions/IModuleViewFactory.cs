using System;
using Avalonia.Controls;

namespace Ecliptix.Core.Core.Abstractions;

public interface IModuleViewFactory
{
    void RegisterView<TViewModel, TView>()
        where TViewModel : class
        where TView : Control, new();

    Control? CreateView(Type viewModelType);
}