using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Ecliptix.Core.Data;

public class EnumDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MembershipViewType viewType)
            return viewType switch
            {
                MembershipViewType.SignIn => "Sign In",
                MembershipViewType.SignUpHost => "Sign Up",
                MembershipViewType.SignUpVerifyMobile => "Verify Mobile",
                MembershipViewType.ForgotPassword => "Forgot Password",
                _ => viewType.ToString()
            };

        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}