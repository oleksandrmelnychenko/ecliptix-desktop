using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Ecliptix.Core.Data;
using Ecliptix.Core.Views.Memberships;

namespace Ecliptix.Core.ViewModels.Utilities;

public static class MembershipViewFactory
{
    private static readonly Dictionary<MembershipViewType, Func<UserControl>> Views
        = new()
        {
            { MembershipViewType.SignIn, () => new SignInView() },
            { MembershipViewType.SignUp, () => new SignUpView() },
            { MembershipViewType.ForgotPassword, () => new ForgotPasswordView() },
        };

    public static UserControl Create(MembershipViewType type)
    {
        if (Views.TryGetValue(type, out Func<UserControl>? ctor))
            return ctor();
        throw new InvalidOperationException($"No view registered for {type}");
    }
}