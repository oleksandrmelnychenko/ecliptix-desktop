using System;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.ViewModels.Memberships;
using ReactiveUI;

namespace Ecliptix.Core.Views.Memberships;

public partial class WelcomeView : ReactiveUserControl<WelcomeViewModel>
{
    public WelcomeView()
    {
        AvaloniaXamlLoader.Load(this);

        // The carousel now handles all its own logic internally
        // No need for manual indicator management or tap handling
        this.WhenActivated(disposables =>
        {
            // Any additional reactive bindings can go here if needed
        });
    }
}
