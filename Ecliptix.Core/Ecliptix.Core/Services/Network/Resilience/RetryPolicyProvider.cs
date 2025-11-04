using System.Collections.Frozen;
using System.Collections.Generic;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Network.Rpc;

namespace Ecliptix.Core.Services.Network.Resilience;

public sealed class RetryPolicyProvider(RetryStrategyConfiguration retryStrategyConfiguration) : IRetryPolicyProvider
{
    private static readonly FrozenDictionary<RpcServiceType, ServiceRetryConfig> ServiceConfigs =
        new Dictionary<RpcServiceType, ServiceRetryConfig>
        {
            [RpcServiceType.RegisterAppDevice] = new(ShouldRetry: true, ReinitOnCompleteFailure: false),
            [RpcServiceType.CheckMobileNumberAvailability] = new(ShouldRetry: true, ReinitOnCompleteFailure: false),
            [RpcServiceType.ValidateMobileNumber] = new(ShouldRetry: true, ReinitOnCompleteFailure: false),
            [RpcServiceType.InitiateVerification] = new(ShouldRetry: false, ReinitOnCompleteFailure: false),
            [RpcServiceType.VerifyOtp] = new(ShouldRetry: true, ReinitOnCompleteFailure: false),
            [RpcServiceType.RegistrationInit] = new(ShouldRetry: true, ReinitOnCompleteFailure: false),
            [RpcServiceType.RegistrationComplete] = new(ShouldRetry: true, ReinitOnCompleteFailure: true),
            [RpcServiceType.SignInInitRequest] = new(ShouldRetry: true, ReinitOnCompleteFailure: false),
            [RpcServiceType.SignInCompleteRequest] = new(ShouldRetry: true, ReinitOnCompleteFailure: true),
            [RpcServiceType.Logout] = new(ShouldRetry: false, ReinitOnCompleteFailure: false),
            [RpcServiceType.AnonymousLogout] = new(ShouldRetry: false, ReinitOnCompleteFailure: false),
            [RpcServiceType.EstablishAuthenticatedSecureChannel] = new(ShouldRetry: true, ReinitOnCompleteFailure: true),
            [RpcServiceType.RecoverySecretKeyInit] = new(ShouldRetry: true, ReinitOnCompleteFailure: false),
            [RpcServiceType.RecoverySecretKeyComplete] = new(ShouldRetry: true, ReinitOnCompleteFailure: true),
        }.ToFrozenDictionary();

    private sealed record ServiceRetryConfig(bool ShouldRetry, bool ReinitOnCompleteFailure);

    public RetryBehavior GetRetryBehavior(RpcServiceType serviceType)
    {
        if (!ServiceConfigs.TryGetValue(serviceType, out ServiceRetryConfig? config))
        {
            return RetryBehavior.NoRetry;
        }

        return new RetryBehavior
        {
            ShouldRetry = config.ShouldRetry,
            MAX_ATTEMPTS = retryStrategyConfiguration.MAX_RETRIES,
            ReinitOnCompleteFailure = config.ReinitOnCompleteFailure
        };
    }
}
