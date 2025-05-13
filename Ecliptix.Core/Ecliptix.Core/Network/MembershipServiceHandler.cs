using System.Collections.Generic;
using Ecliptix.Core.Protobuf.VerificationServices;
using Ecliptix.Protobuf.CipherPayload;
using Grpc.Core;

namespace Ecliptix.Core.Network;

public class MembershipServiceHandler(AuthenticationServices.AuthenticationServicesClient verificationClient)
{
    public async IAsyncEnumerable<CipherPayload> GetVerificationSessionIfExist(CipherPayload request)
    {
        using AsyncServerStreamingCall<CipherPayload>? streamingCall =
            verificationClient.InitiateVerification(request);

        await foreach (CipherPayload response in streamingCall.ResponseStream.ReadAllAsync()) yield return response;
    }
}