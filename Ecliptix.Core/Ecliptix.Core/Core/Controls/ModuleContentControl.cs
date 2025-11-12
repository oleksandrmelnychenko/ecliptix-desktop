using System;
using Avalonia;
using Avalonia.Controls;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Utilities;
using Splat;

namespace Ecliptix.Core.Core.Controls;

public sealed class ModuleContentControl : ContentControl
{
    private readonly IModuleViewFactory? _moduleViewFactory;

    public static readonly StyledProperty<object?> ViewModelContentProperty =
        AvaloniaProperty.Register<ModuleContentControl, object?>(nameof(ViewModelContent));

    public object? ViewModelContent
    {
        get => GetValue(ViewModelContentProperty);
        set => SetValue(ViewModelContentProperty, value);
    }

    public ModuleContentControl()
    {
        try
        {
            _moduleViewFactory = Locator.Current?.GetService<IModuleViewFactory>();
        }
        catch
        {
            _moduleViewFactory = null;
        }
    }

    static ModuleContentControl()
    {
        ViewModelContentProperty.Changed.AddClassHandler<ModuleContentControl>((control, e) =>
            control.OnViewModelContentChanged(e.NewValue));
    }

    private void OnViewModelContentChanged(object? newViewModel)
    {
        Serilog.Log.Information("[MODULE-CONTENT-CONTROL] OnViewModelContentChanged called with: {Type}", newViewModel?.GetType().Name ?? "null");

        if (newViewModel == null)
        {
            Serilog.Log.Information("[MODULE-CONTENT-CONTROL] ViewModel is null, clearing content");
            Content = null;
            return;
        }

        TryCreateViewWithModuleFactory(newViewModel)
            .Or(() => TryCreateViewWithStaticMapper(newViewModel).ToOption())
            .Match(
                view =>
                {
                    Serilog.Log.Information("[MODULE-CONTENT-CONTROL] ✅ View created: {ViewType} for ViewModel: {VMType}", view.GetType().Name, newViewModel.GetType().Name);
                    view.DataContext = newViewModel;
                    Serilog.Log.Information("[MODULE-CONTENT-CONTROL] DataContext set on view");
                    Content = view;
                    Serilog.Log.Information("[MODULE-CONTENT-CONTROL] Content property set");
                },
                () =>
                {
                    Serilog.Log.Warning("[MODULE-CONTENT-CONTROL] ⚠️ No view found for ViewModel: {Type}, using fallback", newViewModel.GetType().Name);
                    Content = CreateFallbackView();
                });
    }

    private Option<Control> TryCreateViewWithModuleFactory(object viewModel)
    {
        if (_moduleViewFactory == null)
        {
            return Option<Control>.None;
        }

        try
        {
            Option<Control> result = _moduleViewFactory.CreateView(viewModel.GetType());
            return result;
        }
        catch
        {
            return Option<Control>.None;
        }
    }

    private static Control? TryCreateViewWithStaticMapper(object viewModel)
    {
        try
        {
            Control? result = viewModel switch
            {
                Features.Authentication.ViewModels.Hosts.AuthenticationViewModel =>
                    new Features.Authentication.Views.Hosts.AuthenticationView(),
                Features.Main.ViewModels.MasterViewModel =>
                    new Features.Main.Views.MasterView(),
                Features.Authentication.ViewModels.SignIn.SignInViewModel => StaticViewMapper.CreateView(
                    typeof(Features.Authentication.ViewModels.SignIn.SignInViewModel)),
                Features.Authentication.ViewModels.Registration.MobileVerificationViewModel =>
                    StaticViewMapper.CreateView(
                        typeof(
                            Features.Authentication.ViewModels.Registration.MobileVerificationViewModel)),
                Features.Authentication.ViewModels.Registration.VerifyOtpViewModel =>
                    StaticViewMapper.CreateView(
                        typeof(Features.Authentication.ViewModels.Registration.VerifyOtpViewModel)),
                Features.Authentication.ViewModels.Registration.SecureKeyVerifierViewModel =>
                    StaticViewMapper.CreateView(
                        typeof(Features.Authentication.ViewModels.Registration.
                            SecureKeyVerifierViewModel)),
                Features.Authentication.ViewModels.Registration.PassPhaseViewModel =>
                    StaticViewMapper.CreateView(
                        typeof(Features.Authentication.ViewModels.Registration.PassPhaseViewModel)),
                Features.Authentication.ViewModels.Welcome.WelcomeViewModel =>
                    StaticViewMapper.CreateView(
                        typeof(Ecliptix.Core.Features.Authentication.ViewModels.Welcome.WelcomeViewModel)),
                _ => null
            };
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static Control CreateFallbackView()
    {
        return new TextBlock
        {
            Text = "No view registered for this ViewModel type",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
    }
}
