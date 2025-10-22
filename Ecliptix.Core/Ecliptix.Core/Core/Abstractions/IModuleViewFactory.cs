using System;
using Avalonia.Controls;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Core.Abstractions;

public interface IModuleViewFactory
{
    void RegisterView<TViewModel, TView>()
        where TViewModel : class
        where TView : Control, new();

    Option<Control> CreateView(Type viewModelType);
}
