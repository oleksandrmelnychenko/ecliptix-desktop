using Ecliptix.Protobuf.Protocol;

namespace Ecliptix.Feature.Authentication.Services.Abstractions.Security;

public interface IServerPublicKeyProvider
{
    byte[] GetServerPublicKey();

    byte[] GetServerPublicKey(PubKeyExchangeType exchangeType);
}
