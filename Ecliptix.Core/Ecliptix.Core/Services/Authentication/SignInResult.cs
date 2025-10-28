using System;
using Ecliptix.Protocol.System.Sodium;

namespace Ecliptix.Core.Services.Authentication;

public sealed class SignInResult(
    SodiumSecureMemoryHandle masterKeyHandle,
    Ecliptix.Protobuf.Membership.Membership? membership,
    Protobuf.Account.Account? activeAccount = null)
    : IDisposable
{
    public SodiumSecureMemoryHandle? MasterKeyHandle { get; private set; } = masterKeyHandle;
    public Protobuf.Membership.Membership? Membership { get; } = membership;
    public Protobuf.Account.Account? ActiveAccount { get; } = activeAccount;

    public void Dispose()
    {
        MasterKeyHandle?.Dispose();
        MasterKeyHandle = null;
    }
}
