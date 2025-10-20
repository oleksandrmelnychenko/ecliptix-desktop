using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using Ecliptix.Protobuf.ProtocolState;
using Google.Protobuf;

namespace Ecliptix.Core.Infrastructure.Security.Crypto;

internal static class RatchetStateHasher
{
    public static byte[] ComputeRatchetFingerprint(RatchetState ratchetState, uint connectId)
    {
        ArgumentNullException.ThrowIfNull(ratchetState);

        using MemoryStream memoryStream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(memoryStream);

        writer.Write(connectId);
        byte[] ratchetBytes = ratchetState.ToByteArray();
        writer.Write(ratchetBytes);

        byte[] data = memoryStream.ToArray();
        byte[] hash = SHA256.HashData(data);

        return hash;
    }
}
