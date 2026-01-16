using System;
using System.Collections.Generic;
using System.Threading;
using Ecliptix.Feature.Authentication.Services.Abstractions.Security;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Protected.Protocol.Utilities;
using Ecliptix.Protobuf.Protocol;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Feature.Authentication.Services.Security;

public sealed class ServerPublicKeyProvider(NetworkProvider networkProvider) : IServerPublicKeyProvider
{
    private const int EXPECTED_KEY_LENGTH = 32;
    private readonly Lock _lock = new();
    private readonly Dictionary<PubKeyExchangeType, byte[]> _cachedKeys = new();

    public byte[] GetServerPublicKey() =>
        GetServerPublicKey(PubKeyExchangeType.InitialHandshake);

    public byte[] GetServerPublicKey(PubKeyExchangeType exchangeType)
    {
        lock (_lock)
        {
            if (_cachedKeys.TryGetValue(exchangeType, out byte[]? cachedKey))
            {
                return (byte[])cachedKey.Clone();
            }

            int key = (int)exchangeType;
            ByteString keyBytes = ByteString.Empty;

            if (networkProvider.ApplicationInstanceSettings.ServerPublicKeys.TryGetValue(key, out ByteString? mapValue))
            {
                keyBytes = mapValue;
            }
#pragma warning disable CS0612
            else if (exchangeType == PubKeyExchangeType.InitialHandshake &&
                     networkProvider.ApplicationInstanceSettings.ServerPublicKey is { IsEmpty: false } legacyKey)
            {
                keyBytes = legacyKey;
            }
#pragma warning restore CS0612

            byte[] newKey = SecureByteStringInterop.WithByteStringAsSpan(
                keyBytes,
                span => span.ToArray());

            if (newKey.Length != EXPECTED_KEY_LENGTH)
            {
                string errorMessage = $"Server public key for exchange type {exchangeType} has invalid length. " +
                                      $"Expected: {EXPECTED_KEY_LENGTH} bytes, Got: {newKey.Length} bytes. " +
                                      "This indicates the server public key was not properly initialized during authentication. " +
                                      "Please restart the application and sign in again.";

                Log.Error(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            _cachedKeys[exchangeType] = newKey;

            return (byte[])newKey.Clone();
        }
    }
}
