using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Services.Core;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using ReactiveUI;

namespace Ecliptix.Core.Features.Authentication.Views.Hosts;

public partial class AuthenticationView : ReactiveUserControl<AuthenticationViewModel>
{
    public AuthenticationView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
