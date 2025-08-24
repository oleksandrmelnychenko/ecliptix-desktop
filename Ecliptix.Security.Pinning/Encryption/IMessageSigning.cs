using System;
using System.Threading.Tasks;

namespace Ecliptix.Security.Pinning.Encryption;

public interface IMessageSigning
{
    Task<byte[]> SignAsync(ReadOnlyMemory<byte> data, SigningAlgorithm algorithm);
    Task<bool> VerifyAsync(ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> signature, string publicKeyHex);
}