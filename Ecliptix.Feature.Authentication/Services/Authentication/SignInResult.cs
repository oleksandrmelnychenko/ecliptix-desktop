using System.Collections.Generic;
using Ecliptix.Protobuf.Account;
using MembershipProto = Ecliptix.Protobuf.Membership.Membership;
using Ecliptix.Utilities;

namespace Ecliptix.Feature.Authentication.Services.Authentication;

public sealed record SignInResult(
    Option<MembershipProto> Membership,
    Option<Account> ActiveAccount,
    IReadOnlyList<Account> AvailableAccounts);
