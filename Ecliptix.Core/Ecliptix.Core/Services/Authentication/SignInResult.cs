using Ecliptix.Protobuf.Account;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Authentication;

public sealed record SignInResult(
    Option<Protobuf.Membership.Membership> Membership,
    Option<Account> ActiveAccount);
