namespace Ecliptix.Core.Services.Authentication;

public sealed record SignInResult(
    Ecliptix.Protobuf.Membership.Membership? Membership,
    Protobuf.Account.Account? ActiveAccount = null);
