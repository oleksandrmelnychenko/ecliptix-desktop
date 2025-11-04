using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;
using Ecliptix.Core.Services.Network.Rpc;

namespace Ecliptix.Core.Services.Network;

public sealed class OperationTimeoutProvider : IOperationTimeoutProvider
{
#if DEBUG
    private static readonly TimeSpan DefaultTimeout = Timeout.InfiniteTimeSpan;
    private const double MAX_ADAPTIVE_MULTIPLIER = 2.0;
    private const double ADAPTIVE_MULTIPLIER_INCREMENT = 0.5;

    private static readonly FrozenDictionary<RpcServiceType, TimeSpan> ServiceTimeouts =
        new Dictionary<RpcServiceType, TimeSpan>
        {
            [RpcServiceType.InitiateVerification] = Timeout.InfiniteTimeSpan,
            [RpcServiceType.EstablishSecrecyChannel] = Timeout.InfiniteTimeSpan,
            [RpcServiceType.RestoreSecrecyChannel] = Timeout.InfiniteTimeSpan,
            [RpcServiceType.EstablishAuthenticatedSecureChannel] = Timeout.InfiniteTimeSpan,
            [RpcServiceType.RegistrationInit] = Timeout.InfiniteTimeSpan,
            [RpcServiceType.RegistrationComplete] = Timeout.InfiniteTimeSpan,
            [RpcServiceType.RecoverySecretKeyInit] = Timeout.InfiniteTimeSpan,
            [RpcServiceType.RecoverySecretKeyComplete] = Timeout.InfiniteTimeSpan,
            [RpcServiceType.SignInInitRequest] = Timeout.InfiniteTimeSpan,
            [RpcServiceType.SignInCompleteRequest] = Timeout.InfiniteTimeSpan,
            [RpcServiceType.ValidateMobileNumber] = Timeout.InfiniteTimeSpan,
            [RpcServiceType.CheckMobileNumberAvailability] = Timeout.InfiniteTimeSpan,
            [RpcServiceType.VerifyOtp] = Timeout.InfiniteTimeSpan,
            [RpcServiceType.RegisterAppDevice] = Timeout.InfiniteTimeSpan,
            [RpcServiceType.Logout] = Timeout.InfiniteTimeSpan
        }.ToFrozenDictionary();
#else
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private const double MAX_ADAPTIVE_MULTIPLIER = 2.0;
    private const double ADAPTIVE_MULTIPLIER_INCREMENT = 0.5;

    private static readonly FrozenDictionary<RpcServiceType, TimeSpan> ServiceTimeouts =
        new Dictionary<RpcServiceType, TimeSpan>
        {
            [RpcServiceType.InitiateVerification] = TimeSpan.FromSeconds(400),
            [RpcServiceType.EstablishSecrecyChannel] = TimeSpan.FromSeconds(60),
            [RpcServiceType.RestoreSecrecyChannel] = TimeSpan.FromSeconds(60),
            [RpcServiceType.EstablishAuthenticatedSecureChannel] = TimeSpan.FromSeconds(60),
            [RpcServiceType.RegistrationInit] = TimeSpan.FromSeconds(45),
            [RpcServiceType.RegistrationComplete] = TimeSpan.FromSeconds(45),
            [RpcServiceType.RecoverySecretKeyInit] = TimeSpan.FromSeconds(45),
            [RpcServiceType.RecoverySecretKeyComplete] = TimeSpan.FromSeconds(45),
            [RpcServiceType.SignInInitRequest] = TimeSpan.FromSeconds(45),
            [RpcServiceType.SignInCompleteRequest] = TimeSpan.FromSeconds(45),
            [RpcServiceType.ValidateMobileNumber] = TimeSpan.FromSeconds(15),
            [RpcServiceType.CheckMobileNumberAvailability] = TimeSpan.FromSeconds(15),
            [RpcServiceType.VerifyOtp] = TimeSpan.FromSeconds(60),
            [RpcServiceType.RegisterAppDevice] = TimeSpan.FromSeconds(30),
            [RpcServiceType.Logout] = TimeSpan.FromSeconds(20)
        }.ToFrozenDictionary();
#endif

    public TimeSpan GetTimeout(RpcServiceType serviceType, RpcRequestContext? requestContext = null)
    {
        TimeSpan baseTimeout = ServiceTimeouts.GetValueOrDefault(serviceType, DefaultTimeout);

        if (requestContext is { Attempt: > 1 })
        {
            double multiplier = CalculateAdaptiveMultiplier(requestContext.Attempt);
            baseTimeout = TimeSpan.FromMilliseconds(baseTimeout.TotalMilliseconds * multiplier);
        }

        return baseTimeout;
    }

    private static double CalculateAdaptiveMultiplier(int attempt)
    {
        double multiplier = 1.0 + (attempt - 1) * ADAPTIVE_MULTIPLIER_INCREMENT;
        return Math.Min(multiplier, MAX_ADAPTIVE_MULTIPLIER);
    }
}
