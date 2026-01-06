using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Native = Ecliptix.Protocol.System.Native.NativeInterop;

namespace Ecliptix.Protocol.System.Native;

public static class NativeProtocolSystem
{
    public static Result<Unit, EcliptixProtocolFailure> Initialize()
    {
        Native.EcliptixErrorCode result = Native.ecliptix_initialize();
        return result == Native.EcliptixErrorCode.Success
            ? Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value)
            : Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(
                    $"Failed to initialize native protocol: {Native.ErrorCodeToString(result)}"));
    }

    public static void Shutdown() => Native.ecliptix_shutdown();

    public static Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> CreateIdentity()
        => EcliptixIdentityKeysWrapper.Create();

    public static Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> CreateIdentityFromSeed(
        byte[] seed,
        string accountId)
        => EcliptixIdentityKeysWrapper.CreateFromSeed(seed, accountId);

    public static Result<NativeProtocolSession, EcliptixProtocolFailure> CreateSessionAdapter(
        EcliptixIdentityKeysWrapper identity,
        Action<uint>? onProtocolStateChanged = null)
    {
        Result<NativeProtocolSession, EcliptixProtocolFailure> sessionResult = NativeProtocolSession.Create(identity);
        if (sessionResult.IsErr)
        {
            return sessionResult;
        }
        NativeProtocolSession session = sessionResult.Unwrap();
        session.SetEventHandler(onProtocolStateChanged);
        return Result<NativeProtocolSession, EcliptixProtocolFailure>.Ok(session);
    }

    public static string GetVersion() => Native.GetVersion();
}
