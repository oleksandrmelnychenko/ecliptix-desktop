using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protected.Protocol.Native;

public static class NativeProtocolSystem
{
    public static Result<Unit, EcliptixProtocolFailure> Initialize()
    {
        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_initialize();
        return result == NativeInterop.EcliptixErrorCode.Success
            ? Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value)
            : Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(
                    $"Failed to initialize native protocol: {NativeInterop.ErrorCodeToString(result)}"));
    }

    public static void Shutdown() => NativeInterop.ecliptix_shutdown();

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

    public static string GetVersion() => NativeInterop.GetVersion();
}
