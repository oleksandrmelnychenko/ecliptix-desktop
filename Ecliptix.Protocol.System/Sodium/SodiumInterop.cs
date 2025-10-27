using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;
using Sodium;

namespace Ecliptix.Protocol.System.Sodium;

internal static partial class SodiumInterop
{
    private const string LibSodium = ProtocolSystemConstants.Libraries.LibSodium;

    private const int MaxBufferSize = 1_000_000_000;

    private static readonly Result<Unit, SodiumFailure> InitializationResult;

    static SodiumInterop()
    {
        InitializationResult = InitializeSodium();
    }

    public static bool IsInitialized => InitializationResult.IsOk;

    [LibraryImport(LibSodium, EntryPoint = "sodium_init")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int sodium_init();

    [LibraryImport(LibSodium, EntryPoint = "sodium_malloc")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr sodium_malloc(nuint size);

    [LibraryImport(LibSodium, EntryPoint = "sodium_free")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void sodium_free(IntPtr ptr);

    [LibraryImport(LibSodium, EntryPoint = "sodium_memzero")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void sodium_memzero(IntPtr ptr, nuint length);

    [LibraryImport(LibSodium, EntryPoint = "sodium_memcmp")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int sodium_memcmp(byte[] b1, byte[] b2, nuint length);

    private static Result<Unit, SodiumFailure> InitializeSodium()
    {
        return Result<Unit, SodiumFailure>.Try(
            () =>
            {
                int result = sodium_init();
                const int dllImportSuccess = ProtocolSystemConstants.Numeric.DllImportSuccess;
                if (result < dllImportSuccess)
                {
                    throw new InvalidOperationException(SodiumFailureMessages.SodiumInitFailed);
                }
            },
            ex => ex switch
            {
                DllNotFoundException dllEx => SodiumFailure.LibraryNotFound(
                    string.Format(SodiumFailureMessages.LibraryLoadFailed, LibSodium), dllEx),
                InvalidOperationException opEx when opEx.Message.Contains(SodiumExceptionMessagePatterns
                        .SodiumInitPattern) =>
                    SodiumFailure.InitializationFailed(SodiumFailureMessages.InitializationFailed, opEx),
                _ => SodiumFailure.InitializationFailed(SodiumFailureMessages.UnexpectedInitError, ex)
            }
        );
    }

    public static Result<Unit, SodiumFailure> SecureWipe(byte[]? buffer)
    {
        if (!IsInitialized)
        {
            return Result<Unit, SodiumFailure>.Err(
                SodiumFailure.InitializationFailed(SodiumFailureMessages.NotInitialized));
        }

        return Result<byte[], SodiumFailure>
            .FromValue(buffer, SodiumFailure.BufferTooSmall(SodiumFailureMessages.BufferNull))
            .Bind(nonNullBuffer => nonNullBuffer switch
            {
                { Length: 0 } => Result<Unit, SodiumFailure>.Ok(Unit.Value),
                _ => Result<byte[], SodiumFailure>.Validate(
                        nonNullBuffer,
                        buf => buf.Length <= MaxBufferSize,
                        SodiumFailure.BufferTooLarge(
                            string.Format(SodiumFailureMessages.BufferTooLarge, nonNullBuffer.Length, MaxBufferSize)))
                    .Bind(validBuffer => validBuffer.Length <= Constants.SmallBufferThreshold
                        ? WipeSmallBuffer(validBuffer)
                        : WipeLargeBuffer(validBuffer))
            });
    }

    public static Result<(SodiumSecureMemoryHandle skHandle, byte[] pk), EcliptixProtocolFailure> GenerateX25519KeyPair(
        string keyPurpose)
    {
        SodiumSecureMemoryHandle? skHandle = null;

        try
        {
            Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
                SodiumSecureMemoryHandle.Allocate(Constants.X25519PrivateKeySize);
            if (allocResult.IsErr)
            {
                return Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure>.Err(allocResult.UnwrapErr()
                    .ToEcliptixProtocolFailure());
            }

            skHandle = allocResult.Unwrap();

            byte[] skBytes = SodiumCore.GetRandomBytes(Constants.X25519PrivateKeySize);
            try
            {
                Result<Unit, SodiumFailure> writeResult = skHandle.Write(skBytes);
                if (writeResult.IsErr)
                {
                    skHandle.Dispose();
                    return Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure>.Err(writeResult.UnwrapErr()
                        .ToEcliptixProtocolFailure());
                }
            }
            finally
            {
                SecureWipe(skBytes);
            }

            byte[] tempPrivCopy = new byte[Constants.X25519PrivateKeySize];
            try
            {
                Result<Unit, SodiumFailure> readResult = skHandle.Read(tempPrivCopy);
                if (readResult.IsErr)
                {
                    skHandle.Dispose();
                    return Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure>.Err(readResult.UnwrapErr()
                        .ToEcliptixProtocolFailure());
                }

                Result<byte[], EcliptixProtocolFailure> deriveResult = Result<byte[], EcliptixProtocolFailure>.Try(
                    () => ScalarMult.Base(tempPrivCopy),
                    ex => EcliptixProtocolFailure.DeriveKey($"Failed to derive {keyPurpose} public key.", ex));

                if (deriveResult.IsErr)
                {
                    skHandle.Dispose();
                    return Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure>
                        .Err(deriveResult.UnwrapErr());
                }

                byte[] pkBytes = deriveResult.Unwrap();

                if (pkBytes.Length != Constants.X25519PublicKeySize)
                {
                    skHandle.Dispose();
                    SecureWipe(pkBytes);
                    return Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.DeriveKey($"Derived {keyPurpose} public key has incorrect size."));
                }

                return Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure>.Ok((skHandle, pkBytes));
            }
            finally
            {
                SecureWipe(tempPrivCopy);
            }
        }
        catch (Exception ex)
        {
            skHandle?.Dispose();
            return Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.KeyGeneration($"Unexpected error generating {keyPurpose} key pair.", ex));
        }
    }

    public static Result<bool, SodiumFailure> ConstantTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
        {
            return Result<bool, SodiumFailure>.Ok(false);
        }

        if (a.IsEmpty)
        {
            return Result<bool, SodiumFailure>.Ok(true);
        }

        try
        {
            int result = sodium_memcmp(a.ToArray(), b.ToArray(), (UIntPtr)a.Length);
            return Result<bool, SodiumFailure>.Ok(result == ProtocolSystemConstants.Numeric.ZeroValue);
        }
        catch (Exception ex)
        {
            return Result<bool, SodiumFailure>.Err(
                SodiumFailure.ComparisonFailed(ProtocolSystemConstants.ErrorMessages.LibSodiumConstantTimeComparisonFailed, ex));
        }
    }

    private static Result<Unit, SodiumFailure> WipeSmallBuffer(byte[] buffer)
    {
        return Result<Unit, SodiumFailure>.Try(
            () => { Array.Clear(buffer, ProtocolSystemConstants.Numeric.ZeroValue, buffer.Length); },
            ex =>
                SodiumFailure.SecureWipeFailed(
                    string.Format(SodiumFailureMessages.SmallBufferClearFailed, buffer.Length), ex));
    }

    private static Result<Unit, SodiumFailure> WipeLargeBuffer(byte[] buffer)
    {
        GCHandle handle = default;
        return Result<Unit, SodiumFailure>.Try(
            () =>
            {
                handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                IntPtr ptr = handle.AddrOfPinnedObject();
                if (ptr == IntPtr.Zero && buffer.Length > ProtocolSystemConstants.Numeric.ZeroValue)
                {
                    throw new InvalidOperationException(SodiumFailureMessages.AddressOfPinnedObjectFailed);
                }

                sodium_memzero(ptr, (UIntPtr)buffer.Length);
            },
            ex => ex switch
            {
                ArgumentException argEx => SodiumFailure.MemoryPinningFailed(
                    SodiumFailureMessages.PinningFailed, argEx),
                OutOfMemoryException oomEx => SodiumFailure.MemoryPinningFailed(
                    SodiumFailureMessages.InsufficientMemory, oomEx),
                InvalidOperationException opEx when opEx.Message.Contains(SodiumExceptionMessagePatterns
                        .AddressPinnedObjectPattern) =>
                    SodiumFailure.MemoryPinningFailed(SodiumFailureMessages.GetPinnedAddressFailed, opEx),
                _ => SodiumFailure.MemoryPinningFailed(
                    string.Format(SodiumFailureMessages.SecureWipeFailed, buffer.Length), ex)
            },
            () =>
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        );
    }
}
