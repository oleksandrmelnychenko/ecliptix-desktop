using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protocol.System.Native;

/// <summary>
/// Thin convenience layer around the native hybrid/PQ protocol bindings.
/// Keeps initialization, identity creation, session creation, and validation in one place so
/// desktop/mobile clients can move off the managed ratchet without duplicating boilerplate.
/// </summary>
public static class NativeProtocolSystem
{
    public static Result<Unit, EcliptixProtocolFailure> Initialize()
    {
        EcliptixErrorCode result = EcliptixNativeInterop.ecliptix_initialize();
        return result == EcliptixErrorCode.Success
            ? Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value)
            : Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(
                    $"Failed to initialize native protocol: {EcliptixNativeInterop.ErrorCodeToString(result)}"));
    }

    public static void Shutdown() => EcliptixNativeInterop.ecliptix_shutdown();

    public static Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> CreateIdentity()
        => EcliptixIdentityKeysWrapper.Create();

    public static Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> CreateIdentityFromSeed(byte[] seed)
        => EcliptixIdentityKeysWrapper.CreateFromSeed(seed);

    public static Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> CreateIdentityFromSeed(
        byte[] seed,
        string membershipId)
        => EcliptixIdentityKeysWrapper.CreateFromSeed(seed, membershipId);

    public static Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure> CreateSession(
        EcliptixIdentityKeysWrapper identity,
        Action<uint>? onProtocolStateChanged = null)
    {
        Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure> wrapperResult =
            EcliptixProtocolSystemWrapper.Create(identity);
        if (wrapperResult.IsErr)
        {
            return wrapperResult;
        }

        EcliptixProtocolSystemWrapper wrapper = wrapperResult.Unwrap();
        wrapper.SetEventHandler(onProtocolStateChanged);
        return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Ok(wrapper);
    }

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

    public static Result<byte[], EcliptixProtocolFailure> DeriveRootFromOpaqueSessionKey(
        byte[] opaqueSessionKey,
        byte[] userContext)
        => EcliptixProtocolSystemWrapper.DeriveRootFromOpaqueSessionKey(opaqueSessionKey, userContext);

    public static Result<Unit, EcliptixProtocolFailure> ValidateEnvelopeHybridRequirements(byte[] encryptedEnvelope)
        => EcliptixProtocolSystemWrapper.ValidateEnvelopeHybridRequirements(encryptedEnvelope);
}
