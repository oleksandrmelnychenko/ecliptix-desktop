using System.Collections.Generic;
using Ecliptix.Core.Protobuf.VerificationServices;
using Ecliptix.Protobuf.CipherPayload;
using Grpc.Core;

namespace Ecliptix.Core.Network;

public class MembershipServiceHandler(VerificationServiceActions.VerificationServiceActionsClient verificationClient)
{
    public async IAsyncEnumerable<CipherPayload> GetVerificationSessionIfExist(CipherPayload request)
    {
        using AsyncServerStreamingCall<CipherPayload>? streamingCall =
            verificationClient.GetVerificationSessionIfExist(request);

        await foreach (CipherPayload response in streamingCall.ResponseStream.ReadAllAsync())
        {
            yield return response;
        }
    }
}