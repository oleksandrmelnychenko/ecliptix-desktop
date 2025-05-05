using System;
using Avalonia.Controls;

namespace Ecliptix.Core.Services;

public class WindowLocator
{
    public Window CreateWindow(object viewModel)
    {
        var viewModelType = viewModel.GetType();
        var windowName = viewModelType.FullName!.Replace("WindowViewModel", "Window");
        var windowType = Type.GetType(windowName);

        if (windowType != null)
        {
            var window = (Window)Activator.CreateInstance(windowType)!;
            window.DataContext = viewModel;
            return window;
        }

        throw new InvalidOperationException($"Window not found: {windowName}");
    }
}