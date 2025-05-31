using System.Collections.Generic;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.Membership;
using Grpc.Core;

namespace Ecliptix.Core.Network;

public class MembershipServiceHandler(AuthVerificationServices.AuthVerificationServicesClient verificationClient)
{
    public async IAsyncEnumerable<CipherPayload> GetVerificationSessionIfExist(CipherPayload request)
    {
        using AsyncServerStreamingCall<CipherPayload>? streamingCall =
            verificationClient.InitiateVerification(request);

        await foreach (CipherPayload response in streamingCall.ResponseStream.ReadAllAsync()) yield return response;
    }
}