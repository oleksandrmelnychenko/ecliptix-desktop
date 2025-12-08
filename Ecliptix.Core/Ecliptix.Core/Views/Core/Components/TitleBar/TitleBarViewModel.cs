using System;
using System.Collections.ObjectModel;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Views.Core.Components.TitleBar;

public sealed class TitleBarViewModel : ReactiveObject, IDisposable
{
    private bool _isDisposed;

    [Reactive] public bool DisableCloseButton { get; set; }
    [Reactive] public bool DisableMinimizeButton { get; set; }
    [Reactive] public bool DisableMaximizeButton { get; set; }

    public ObservableCollection<object> LeftContent { get; } = new();
    public ObservableCollection<object> RightContent { get; } = new();

    [Reactive] public object? CenterContent { get; set; }

    [Reactive] public bool IsDragging { get; set; }
    [Reactive] public bool IsDraggingEnabled { get; set; } = true;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        foreach (object item in LeftContent)
        {
            if (item is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        foreach (object item in RightContent)
        {
            if (item is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        LeftContent.Clear();
        RightContent.Clear();

        if (CenterContent is IDisposable centerDisposable)
        {
            centerDisposable.Dispose();
        }

        CenterContent = null;
    }
}
