using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Ecliptix.Core.Features.Authentication.ViewModels.SignIn;
using Ecliptix.Core.Features.Authentication.ViewModels.Registration;
using Ecliptix.Core.Features.Authentication.ViewModels.Welcome;
using Ecliptix.Core.Features.Authentication.Views.SignIn;
using Ecliptix.Core.Features.Authentication.Views.Registration;
using Ecliptix.Core.Features.Authentication.Views.Welcome;

namespace Ecliptix.Core.Core.MVVM;

public static class StaticViewMapper
{
    private static readonly FrozenDictionary<Type, Lazy<Func<Control>>> ViewFactories =
        CreateViewFactories().ToFrozenDictionary();

    private static Dictionary<Type, Lazy<Func<Control>>> CreateViewFactories()
    {
        return new Dictionary<Type, Lazy<Func<Control>>>
        {
            [typeof(SignInViewModel)] = new(() => () => new SignInView()),
            [typeof(MobileVerificationViewModel)] = new(() => () => new MobileVerificationView()),
            [typeof(VerifyOtpViewModel)] = new(() => () => new VerificationCodeEntryView()),
            [typeof(PasswordConfirmationViewModel)] = new(() => () => new PasswordConfirmationView()),
            [typeof(PassPhaseViewModel)] = new(() => () => new PassPhaseView()),
            [typeof(WelcomeViewModel)] = new(() => () => new WelcomeView())
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Control? CreateView(Type viewModelType)
    {
        Serilog.Log.Information("🔍 StaticViewMapper.CreateView: Looking for factory for {ViewModelType}",
            viewModelType.FullName);
        if (ViewFactories.TryGetValue(viewModelType, out Lazy<Func<Control>>? lazyFactory))
        {
            try
            {
                Func<Control> factory = lazyFactory.Value;
                Control result = factory();
                Serilog.Log.Information(
                    "✅ StaticViewMapper.CreateView: Successfully created {ViewType} for {ViewModelType}",
                    result.GetType().Name, viewModelType.FullName);
                return result;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "❌ StaticViewMapper.CreateView: Exception creating view for {ViewModelType}",
                    viewModelType.FullName);
                return null;
            }
        }

        Serilog.Log.Warning("❌ StaticViewMapper.CreateView: No factory found for {ViewModelType}",
            viewModelType.FullName);
        return null;
    }

}