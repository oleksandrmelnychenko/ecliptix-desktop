using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;

namespace Ecliptix.Core.Data;

public class EnumDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AuthViewType viewType)
            return viewType switch
            {
                AuthViewType.SignIn => "Sign In",
                AuthViewType.RegistrationWizard => "Sign Up",
                AuthViewType.PhoneVerification => "Verify Mobile",
                AuthViewType.PasswordRecovery => "Forgot Password",
                _ => viewType.ToString()
            };

        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}