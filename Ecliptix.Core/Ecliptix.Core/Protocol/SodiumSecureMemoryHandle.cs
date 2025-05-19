using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ecliptix.Core.Protocol.Utilities;

namespace Ecliptix.Core.Protocol;

public sealed class SodiumSecureMemoryHandle : SafeHandle
{
    private SodiumSecureMemoryHandle(IntPtr preexistingHandle, int length, bool ownsHandle)
        : base(IntPtr.Zero, ownsHandle)
    {
        SetHandle(preexistingHandle);
        Length = length;
    }

    public int Length { get; }

    public override bool IsInvalid => handle == IntPtr.Zero;

    public static Result<SodiumSecureMemoryHandle, ShieldFailure> Allocate(int length)
    {
        switch (length)
        {
            case < 0:
                return Result<SodiumSecureMemoryHandle, ShieldFailure>.Err(
                    ShieldFailure.InvalidInput($"Requested allocation length cannot be negative ({length})."));
            case 0:
                return Result<SodiumSecureMemoryHandle, ShieldFailure>.Ok(
                    new SodiumSecureMemoryHandle(IntPtr.Zero, 0, true));
        }

        if (!SodiumInterop.IsInitialized)
            return Result<SodiumSecureMemoryHandle, ShieldFailure>.Err(
                ShieldFailure.Generic("SodiumInterop is not initialized."));

        UIntPtr size = (UIntPtr)length;
        IntPtr ptr = IntPtr.Zero;

        try
        {
            ptr = SodiumInterop.sodium_malloc(size);
            if (ptr == IntPtr.Zero)
                return Result<SodiumSecureMemoryHandle, ShieldFailure>.Err(
                    ShieldFailure.AllocationFailed($"sodium_malloc failed to allocate {length} bytes."));

            return Result<SodiumSecureMemoryHandle, ShieldFailure>.Ok(
                new SodiumSecureMemoryHandle(ptr, length, true));
        }
        catch (Exception ex)
        {
            if (ptr != IntPtr.Zero)
                SodiumInterop.sodium_free(ptr);

            return Result<SodiumSecureMemoryHandle, ShieldFailure>.Err(
                ShieldFailure.AllocationFailed($"Unexpected error during allocation ({length} bytes).", ex));
        }
    }

    public Result<Unit, ShieldFailure> Write(ReadOnlySpan<byte> data)
    {
        if (IsInvalid || IsClosed)
            return Result<Unit, ShieldFailure>.Err(ShieldFailure.ObjectDisposed(nameof(SodiumSecureMemoryHandle)));

        if (data.Length > Length)
            return Result<Unit, ShieldFailure>.Err(
                ShieldFailure.DataTooLarge($"Data length ({data.Length}) exceeds allocated buffer size ({Length})."));

        if (data.IsEmpty)
            return Result<Unit, ShieldFailure>.Ok(Unit.Value);

        bool success = false;
        try
        {
            DangerousAddRef(ref success);
            if (!success)
                return Result<Unit, ShieldFailure>.Err(ShieldFailure.Generic("Failed to increment reference count."));

            if (IsInvalid || IsClosed)
                return Result<Unit, ShieldFailure>.Err(
                    ShieldFailure.ObjectDisposed($"{nameof(SodiumSecureMemoryHandle)} disposed after AddRef."));

            unsafe
            {
                Buffer.MemoryCopy(
                    Unsafe.AsPointer(ref MemoryMarshal.GetReference(data)),
                    (void*)handle,
                    (ulong)Length,
                    (ulong)data.Length);
            }

            return Result<Unit, ShieldFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, ShieldFailure>.Err(
                ShieldFailure.DataAccess("Unexpected error during write operation.", ex));
        }
        finally
        {
            if (success)
                DangerousRelease();
        }
    }

    public Result<Unit, ShieldFailure> Read(Span<byte> destination)
    {
        if (IsInvalid || IsClosed)
            return Result<Unit, ShieldFailure>.Err(ShieldFailure.ObjectDisposed(nameof(SodiumSecureMemoryHandle)));

        if (destination.Length < Length)
            return Result<Unit, ShieldFailure>.Err(
                ShieldFailure.BufferTooSmall(
                    $"Destination buffer size ({destination.Length}) is smaller than the allocated size ({Length})."));

        if (Length == 0)
            return Result<Unit, ShieldFailure>.Ok(Unit.Value);

        bool success = false;
        try
        {
            DangerousAddRef(ref success);
            if (!success)
                return Result<Unit, ShieldFailure>.Err(ShieldFailure.Generic("Failed to increment reference count."));

            if (IsInvalid || IsClosed)
                return Result<Unit, ShieldFailure>.Err(
                    ShieldFailure.ObjectDisposed($"{nameof(SodiumSecureMemoryHandle)} disposed after AddRef."));

            unsafe
            {
                Buffer.MemoryCopy(
                    (void*)handle,
                    Unsafe.AsPointer(ref MemoryMarshal.GetReference(destination)),
                    (ulong)destination.Length,
                    (ulong)Length);
            }

            return Result<Unit, ShieldFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, ShieldFailure>.Err(
                ShieldFailure.DataAccess("Unexpected error during read operation.", ex));
        }
        finally
        {
            if (success)
                DangerousRelease();
        }
    }

    public Result<byte[], ShieldFailure> ReadBytes(int length)
    {
        if (IsInvalid || IsClosed)
            return Result<byte[], ShieldFailure>.Err(ShieldFailure.ObjectDisposed(nameof(SodiumSecureMemoryHandle)));

        if (length < 0)
            return Result<byte[], ShieldFailure>.Err(
                ShieldFailure.InvalidInput($"Requested read length cannot be negative ({length})."));

        if (length > Length)
            return Result<byte[], ShieldFailure>.Err(
                ShieldFailure.BufferTooSmall($"Requested read length ({length}) exceeds allocated size ({Length})."));

        if (length == 0)
            return Result<byte[], ShieldFailure>.Ok([]);

        byte[] buffer = new byte[length];
        bool success = false;
        try
        {
            DangerousAddRef(ref success);
            if (!success)
                return Result<byte[], ShieldFailure>.Err(ShieldFailure.Generic("Failed to increment reference count."));

            if (IsInvalid || IsClosed)
                return Result<byte[], ShieldFailure>.Err(
                    ShieldFailure.ObjectDisposed($"{nameof(SodiumSecureMemoryHandle)} disposed after AddRef."));

            unsafe
            {
                Buffer.MemoryCopy(
                    (void*)handle,
                    Unsafe.AsPointer(ref MemoryMarshal.GetReference(buffer.AsSpan())),
                    (ulong)length,
                    (ulong)length);
            }

            return Result<byte[], ShieldFailure>.Ok(buffer);
        }
        catch (Exception ex)
        {
            return Result<byte[], ShieldFailure>.Err(
                ShieldFailure.DataAccess($"Unexpected error reading {length} bytes.", ex));
        }
        finally
        {
            if (success)
                DangerousRelease();
        }
    }

    protected override bool ReleaseHandle()
    {
        if (IsInvalid) return true;

        if (!SodiumInterop.IsInitialized) return false;

        SodiumInterop.sodium_free(handle);
        SetHandleAsInvalid();
        return true;
    }
}