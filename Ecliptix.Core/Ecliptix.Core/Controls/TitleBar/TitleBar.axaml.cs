using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Shell.Constants;
using ReactiveUI;

namespace Ecliptix.Core.Controls.TitleBar;

public partial class TitleBar : ReactiveUserControl<TitleBarViewModel>, IDisposable
{
    private readonly ContentControl? _rootControl;
    private readonly CompositeDisposable _disposables = new();
    private IDisposable? _dataContextBinding;
    private bool _isDisposed;

    public TitleBar()
    {
        InitializeComponent();
        _rootControl = this.FindControl<ContentControl>("PART_Root");

        InitializeLayout();
    }

    private void InitializeLayout()
    {
        if (_rootControl == null || _rootControl.Content != null)
        {
            return;
        }

        UserControl layout = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? new Platform.OSX.MacosTitleBarLayout()
            : new Platform.Windows.WindowsTitleBarLayout();

        _dataContextBinding = layout.Bind(DataContextProperty, this.GetObservable(DataContextProperty));

        _rootControl.Content = layout;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        _dataContextBinding?.Dispose();
        _disposables.Dispose();

        if (_rootControl?.Content is IDisposable disposableContent)
        {
            disposableContent.Dispose();
        }
    }
}
