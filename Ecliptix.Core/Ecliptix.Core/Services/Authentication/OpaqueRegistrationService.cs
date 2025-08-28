using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;

namespace Ecliptix.Core.Services.Authentication;

public class OpaqueRegistrationService(
    NetworkProvider networkProvider,
    ILocalizationService localizationService,
    ISystemEventService systemEventService)
    : IRegistrationService
{
    private readonly NetworkProvider _networkProvider = networkProvider;
    private readonly ILocalizationService _localizationService = localizationService;
    private readonly ISystemEventService _systemEventService = systemEventService;
    

    public async Task<Result<ByteString, string>> ValidatePhoneNumberAsync(
        string mobileNumber, 
        string deviceIdentifier,
        uint connectId)
    {
        if (string.IsNullOrEmpty(mobileNumber))
        {
            return Result<ByteString, string>.Err(
                _localizationService["ValidationErrors.MobileNumber.Required"]);
        }

        ValidatePhoneNumberRequest request = new()
        {
            MobileNumber = mobileNumber,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(deviceIdentifier))
        };

        TaskCompletionSource<ByteString> responseSource = new();

        Result<Unit, NetworkFailure> networkResult = await _networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.ValidatePhoneNumber,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()),
            async payload =>
            {
                try
                {
                    ValidatePhoneNumberResponse response = Helpers.ParseFromBytes<ValidatePhoneNumberResponse>(payload);
                    
                    if (response.Result == VerificationResult.InvalidPhone)
                    {
                        responseSource.TrySetException(new InvalidOperationException(response.Message));
                    }
                    else
                    {
                        responseSource.TrySetResult(response.MobileNumberIdentifier);
                    }
                    
                    return Result<Unit, NetworkFailure>.Ok(Unit.Value);
                }
                catch (Exception ex)
                {
                    responseSource.TrySetException(ex);
                    return Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.DataCenterNotResponding($"Failed to parse response: {ex.Message}"));
                }
            }, true, CancellationToken.None);

        if (networkResult.IsErr)
        {
            return Result<ByteString, string>.Err(networkResult.UnwrapErr().Message);
        }

        try
        {
            ByteString identifier = await responseSource.Task;
            return Result<ByteString, string>.Ok(identifier);
        }
        catch (Exception ex)
        {
            return Result<ByteString, string>.Err(ex.Message);
        }
    }
    
    
    public Task<Result<Guid, string>> InitiateOtpVerificationAsync(ByteString phoneNumberIdentifier, string deviceIdentifier)
    {
        throw new NotImplementedException();
    }

    public Task<Result<Protobuf.Membership.Membership, string>> VerifyOtpAsync(string otpCode, Guid sessionIdentifier, string deviceIdentifier)
    {
        throw new NotImplementedException();
    }

    public Task<Result<Unit, string>> CompleteRegistrationAsync(ByteString membershipIdentifier, SecureTextBuffer secureKey)
    {
        throw new NotImplementedException();
    }
    
}