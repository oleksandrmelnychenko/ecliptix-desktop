using System;
using System.Runtime.InteropServices;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.CertificatePinning;

namespace Ecliptix.Security.Certificate.Pinning.Native;

internal static class SafeNativeOperations
{
    public static Result<T, CertificatePinningFailure> ExecuteNativeOperation<T>(Func<T> operation, string operationName)
    {
        try
        {
            return Result<T, CertificatePinningFailure>.Ok(operation());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return Result<T, CertificatePinningFailure>.Err(CertificatePinningFailure.NativeLibraryNotFound(operationName, ex));
        }
        catch (Exception ex)
        {
            return Result<T, CertificatePinningFailure>.Err(CertificatePinningFailure.NativeOperationFailed(operationName, ex));
        }
    }

    public static unsafe string GetErrorString(CertificatePinningNativeResult result)
    {
        try
        {
            byte* errorPtr = CertificatePinningNativeLibrary.GetErrorMessage();
            return errorPtr != null
                ? Marshal.PtrToStringUTF8((IntPtr)errorPtr) ?? $"Unknown error: {result}"
                : $"Error code: {result}";
        }
        catch
        {
            return $"Error code: {result}";
        }
    }
}