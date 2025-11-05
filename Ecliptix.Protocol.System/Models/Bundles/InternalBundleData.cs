using Ecliptix.Protocol.System.Models.Keys;

namespace Ecliptix.Protocol.System.Models.Bundles;

internal readonly struct InternalBundleData
{
    public required byte[] IdentityEd25519 { get; init; }
    public required byte[] IdentityX25519 { get; init; }
    public required uint SignedPreKeyId { get; init; }
    public required byte[] SignedPreKeyPublic { get; init; }
    public required byte[] SignedPreKeySignature { get; init; }
    public required List<OneTimePreKeyRecord> OneTimePreKeys { get; init; }
    public required byte[]? EphemeralX25519 { get; init; }
}
