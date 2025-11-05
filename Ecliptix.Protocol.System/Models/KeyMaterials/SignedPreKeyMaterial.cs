using Ecliptix.Protocol.System.Sodium;

namespace Ecliptix.Protocol.System.Models.KeyMaterials;

internal readonly record struct SignedPreKeyMaterial(
    uint Id,
    SodiumSecureMemoryHandle SecretKeyHandle,
    byte[] PublicKey,
    byte[] Signature);
