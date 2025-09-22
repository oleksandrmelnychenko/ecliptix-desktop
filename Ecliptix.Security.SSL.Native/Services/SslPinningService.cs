using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Security.SSL.Native.Native;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.SslPinning;

namespace Ecliptix.Security.SSL.Native.Services;

public sealed class SslPinningService : IDisposable, IAsyncDisposable
{
    private int _initialized;
    private int _disposed;

    private bool IsInitialized => Volatile.Read(ref _initialized) == 1;
    private bool IsDisposed => Volatile.Read(ref _disposed) == 1;

    public Task<Result<Unit, SslPinningFailure>> InitializeAsync(CancellationToken cancellationToken = default)
        => Task.Run(InitializeSync, cancellationToken);

    private Result<Unit, SslPinningFailure> InitializeSync()
    {
        if (IsDisposed)
            return Result<Unit, SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (IsInitialized)
            return Result<Unit, SslPinningFailure>.Ok(Unit.Value);

        try
        {
            EcliptixResult result = EcliptixNativeLibrary.Initialize();
            if (result != EcliptixResult.Success)
            {
                string error = GetErrorString(result);
                return Result<Unit, SslPinningFailure>.Err(SslPinningFailure.LibraryInitializationFailed(error));
            }

            Volatile.Write(ref _initialized, 1);
            return Result<Unit, SslPinningFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, SslPinningFailure>.Err(SslPinningFailure.InitializationException(ex));
        }
    }

    public Task<Result<bool, SslPinningFailure>> VerifyServerSignatureAsync(
        byte[] data,
        byte[] signature,
        CancellationToken cancellationToken = default)
        => Task.Run(() => VerifyServerSignatureSync(data, signature), cancellationToken);

    private Result<bool, SslPinningFailure> VerifyServerSignatureSync(byte[] data, byte[] signature)
    {
        if (IsDisposed)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (!IsInitialized)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (data == null || data.Length == 0)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.MessageRequired());

        if (signature == null || signature.Length == 0)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.InvalidSignatureSize(0));

        try
        {
            unsafe
            {
                fixed (byte* dataPtr = data)
                fixed (byte* signaturePtr = signature)
                {
                    EcliptixResult result = EcliptixNativeLibrary.VerifySignature(
                        dataPtr, (nuint)data.Length,
                        signaturePtr, (nuint)signature.Length);

                    return result switch
                    {
                        EcliptixResult.Success => Result<bool, SslPinningFailure>.Ok(true),
                        EcliptixResult.ErrorVerificationFailed => Result<bool, SslPinningFailure>.Ok(false),
                        _ => Result<bool, SslPinningFailure>.Err(
                            SslPinningFailure.Ed25519VerificationError(GetErrorString(result)))
                    };
                }
            }
        }
        catch (Exception ex)
        {
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.Ed25519VerificationException(ex));
        }
    }

    private static unsafe string GetErrorString(EcliptixResult result)
    {
        try
        {
            byte* errorPtr = EcliptixNativeLibrary.GetErrorMessage();
            if (errorPtr != null)
            {
                return Marshal.PtrToStringUTF8((IntPtr)errorPtr) ?? $"Unknown error: {result}";
            }
        }
        catch
        {
        }

        return $"Error code: {result}";
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        try
        {
            if (IsInitialized)
            {
                EcliptixNativeLibrary.Cleanup();
            }
        }
        catch
        {
        }
        finally
        {
            Volatile.Write(ref _initialized, 0);
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}