using System;
using Avalonia;
using Avalonia.Controls;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Core.Abstractions;
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
        Serilog.Log.Information(
            "🔍 ModuleContentControl.OnViewModelContentChanged called with ViewModel: {ViewModelType}",
            newViewModel?.GetType().Name ?? "null");
        if (newViewModel == null)
        {
            Content = null;
            return;
        }

        Control? view = TryCreateViewWithModuleFactory(newViewModel) ?? TryCreateViewWithStaticMapper(newViewModel);
        Serilog.Log.Information("🔍 ModuleContentControl: StaticViewMapper result: {ViewType}",
            view?.GetType().Name ?? "null");
        if (view != null)
        {
            view.DataContext = newViewModel;
            Content = view;
            Serilog.Log.Information(
                "✅ ModuleContentControl: Successfully set view: {ViewType} for ViewModel: {ViewModelType}",
                view.GetType().Name, newViewModel.GetType().Name);
        }
        else
        {
            Content = CreateFallbackView();
            Serilog.Log.Warning("❌ ModuleContentControl: No view found, using fallback for ViewModel: {ViewModelType}",
                newViewModel.GetType().Name);
        }
    }

    private Control? TryCreateViewWithModuleFactory(object viewModel)
    {
        if (_moduleViewFactory == null) return null;

        try
        {
            Serilog.Log.Information("🔍 TryCreateViewWithModuleFactory: Attempting to create view for {ViewModelType}",
                viewModel.GetType().FullName);

            Control? result = _moduleViewFactory.CreateView(viewModel.GetType());

            if (result != null)
            {
                Serilog.Log.Information(
                    "✅ ModuleViewFactory: Successfully created {ViewType} for {ViewModelType}",
                    result.GetType().Name, viewModel.GetType().Name);
            }
            else
            {
                Serilog.Log.Debug(
                    "🔍 ModuleViewFactory: No factory registered for {ViewModelType}",
                    viewModel.GetType().Name);
            }

            return result;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "❌ TryCreateViewWithModuleFactory: Exception creating view for {ViewModelType}",
                viewModel.GetType().FullName);
            return null;
        }
    }

    private static Control? TryCreateViewWithStaticMapper(object viewModel)
    {
        Serilog.Log.Information("🔍 TryCreateViewWithStaticMapper: Attempting to create view for {ViewModelType}",
            viewModel.GetType().FullName);
        try
        {
            Control? result = viewModel switch
            {
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
                        typeof(Features.Authentication.ViewModels.Welcome.WelcomeViewModel)),
                _ => null
            };
            Serilog.Log.Information(
                "🔍 TryCreateViewWithStaticMapper: Pattern matching result for {ViewModelType}: {ViewType}",
                viewModel.GetType().FullName, result?.GetType().Name ?? "null");
            return result;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "❌ TryCreateViewWithStaticMapper: Exception creating view for {ViewModelType}",
                viewModel.GetType().FullName);
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
