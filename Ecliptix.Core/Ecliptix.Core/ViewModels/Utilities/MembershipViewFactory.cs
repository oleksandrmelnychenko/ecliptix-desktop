using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Ecliptix.Core.Data;
using Ecliptix.Core.ViewModels.Memberships;
using Ecliptix.Core.Views.Memberships;
using Ecliptix.Core.Views.Memberships.SignUp;
using Splat;

namespace Ecliptix.Core.ViewModels.Utilities;

public class MembershipViewFactory
{
    private readonly Dictionary<MembershipViewType, Func<UserControl>> Views
        = new()
        {
            { MembershipViewType.SignIn, () => new SignInView(Locator.Current.GetService<SignInViewModel>()!) },
            { MembershipViewType.SignUp, () => new SignUpHostView(Locator.Current.GetService<SignUpHostViewModel>()!) },
            { MembershipViewType.ForgotPassword, () => new ForgotPasswordView() },
        };


    public MembershipViewFactory()
    {
    }
    
    public UserControl Create(MembershipViewType type)
    {
        if (Views.TryGetValue(type, out Func<UserControl>? ctor))
            return ctor();
        throw new InvalidOperationException($"No view registered for {type}");
    }
}