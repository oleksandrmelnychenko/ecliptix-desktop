using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Authentication;

public sealed record SignInResult(
    Option<Ecliptix.Protobuf.Membership.Membership> Membership,
    Option<Ecliptix.Protobuf.Account.Account> ActiveAccount);
