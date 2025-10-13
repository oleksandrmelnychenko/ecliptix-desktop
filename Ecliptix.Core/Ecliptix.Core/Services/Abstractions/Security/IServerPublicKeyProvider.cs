namespace Ecliptix.Core.Services.Abstractions.Security;

public interface IServerPublicKeyProvider
{
    byte[] GetServerPublicKey();
}
