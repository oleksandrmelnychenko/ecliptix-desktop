using Ecliptix.Protocol.System.Sodium;

namespace Ecliptix.Protocol.System.Models.KeyMaterials;

internal readonly record struct Ed25519KeyMaterial(
    SodiumSecureMemoryHandle SecretKeyHandle,
    byte[] PublicKey);
