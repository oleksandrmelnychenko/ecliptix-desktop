using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Ecliptix.Core.Features.Authentication.ViewModels.SignIn;
using Ecliptix.Core.Features.Authentication.ViewModels.Registration;
using Ecliptix.Core.Features.Authentication.ViewModels.Welcome;
using Ecliptix.Core.Features.Authentication.ViewModels.PasswordRecovery;
using Ecliptix.Core.Features.Authentication.Views.SignIn;
using Ecliptix.Core.Features.Authentication.Views.Registration;
using Ecliptix.Core.Features.Authentication.Views.Welcome;
using Ecliptix.Core.Features.Authentication.Views.PasswordRecovery;

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
            [typeof(SecureKeyVerifierViewModel)] = new(() => () => new SecureKeyConfirmationView()),
            [typeof(PassPhaseViewModel)] = new(() => () => new PassPhaseView()),
            [typeof(WelcomeViewModel)] = new(() => () => new WelcomeView()),
            [typeof(ForgotPasswordResetViewModel)] = new(() => () => new ForgotPasswordResetView())
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Control? CreateView(Type viewModelType)
    {
        if (!ViewFactories.TryGetValue(viewModelType, out Lazy<Func<Control>>? lazyFactory)) return null;
        Func<Control> factory = lazyFactory.Value;
        Control result = factory();
        return result;
    }
}