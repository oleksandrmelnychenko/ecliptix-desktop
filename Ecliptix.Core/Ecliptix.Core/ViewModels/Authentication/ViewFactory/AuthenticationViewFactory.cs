using System;
using Avalonia.Controls;
using Ecliptix.Core.ViewModels.Authentication.Registration;
using Ecliptix.Core.ViewModels.Memberships;
using Ecliptix.Core.Views.Authentication;
using Ecliptix.Core.Views.Authentication.Registration;
using Splat;

namespace Ecliptix.Core.ViewModels.Authentication.ViewFactory;

public sealed class AuthenticationViewFactory(IReadonlyDependencyResolver? resolver = null!)
{
    private readonly IReadonlyDependencyResolver _resolver = resolver ?? Locator.Current;

    public UserControl Create(AuthViewType screenType) => screenType switch
    {
        AuthViewType.SignIn => new SignInView(
            _resolver.GetRequiredService<SignInViewModel>()),

        AuthViewType.RegistrationWizard => new RegistrationWizardView(
            _resolver.GetRequiredService<RegistrationWizardViewModel>()),

        AuthViewType.PhoneVerification => new PhoneVerificationView(
            _resolver.GetRequiredService<PhoneVerificationViewModel>()),

        AuthViewType.VerificationCodeEntry => new VerificationCodeEntryView(
            _resolver.GetRequiredService<VerificationCodeEntryViewModel>()),
        
        AuthViewType.ConfirmPassword => new ConfirmPasswordView(
            _resolver.GetRequiredService<ConfirmPasswordViewModel>()),

        _ => throw new ArgumentOutOfRangeException(
            nameof(screenType), screenType, "Unregistered authentication view type.")
    };
}

public static class DependencyResolverExtensions
{
    public static T GetRequiredService<T>(this IReadonlyDependencyResolver resolver) =>
        resolver.GetService<T>() ?? throw new InvalidOperationException(
            $"Dependency of type '{typeof(T)}' is not registered.");
}