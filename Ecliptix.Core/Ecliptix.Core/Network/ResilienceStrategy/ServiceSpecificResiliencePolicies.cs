using System;
using System.Threading.Tasks;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Serilog;

namespace Ecliptix.Core.Network.ResilienceStrategy;

public static class ServiceSpecificResiliencePolicies
{
    // NEW ADDED 2025-07-07 16:12 by Vitalik Koliesnikov
    public static async Task<Result<Unit, NetworkFailure>> ExecuteWithRetryAndRecovery<T>(
        Func<Task<Result<T, NetworkFailure>>> operation,
        Func<RcpServiceType, uint, Task<Result<Unit, NetworkFailure>>> recoveryHandler,
        RcpServiceType serviceType,
        uint connectId,
        int maxAttempts = 3)
    {
        int attempt = 0;

        while (attempt < maxAttempts)
        {
            attempt++;

            Result<T, NetworkFailure> result = await operation();

            if (result.IsOk)
            {
                if (attempt > 1)
                {
                    Log.Information("Request succeeded on attempt {Attempt}", attempt);
                }
                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            }

            NetworkFailure failure = result.UnwrapErr();
            Log.Warning("Request attempt {Attempt}/{MaxAttempts} failed: {Error}",
                attempt, maxAttempts, failure.Message);

            // Attempt recovery except on the last attempt
            if (attempt < maxAttempts)
            {
                Log.Information("Attempting session recovery for service type: {ServiceType}", serviceType);

                Result<Unit, NetworkFailure> recoveryResult = await recoveryHandler(serviceType, connectId);

                if (recoveryResult.IsOk)
                {
                    Log.Information("Recovery successful, retrying request");
                    
                    // Add exponential backoff delay
                    TimeSpan delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                    Log.Information("Waiting {DelayMs}ms before retry...", delay.TotalMilliseconds);
                    await Task.Delay(delay);
                    
                    continue;
                }
                else
                {
                    Log.Error("Recovery failed: {Error}", recoveryResult.UnwrapErr().Message);
                }
            }

            if (attempt >= maxAttempts)
            {
                Log.Error("Request failed after {MaxAttempts} attempts. Final error: {Error}",
                    maxAttempts, failure.Message);
                return Result<Unit, NetworkFailure>.Err(failure);
            }
        }

        return Result<Unit, NetworkFailure>.Err(
            NetworkFailure.InvalidRequestType($"Request failed after {maxAttempts} attempts"));
    }
}