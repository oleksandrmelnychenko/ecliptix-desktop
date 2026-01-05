using Ecliptix.Protobuf.Transport.Identity;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Authentication;

public sealed record SignInResult(
    Option<Protobuf.Transport.Identity.Membership> Membership,
    Option<Account> ActiveAccount);
