using Ecliptix.Protocol.System.Models.Keys;

namespace Ecliptix.Protocol.System.Models.KeyMaterials;

internal readonly record struct IdentityKeysMaterial(
    Ed25519KeyMaterial Ed25519,
    X25519KeyMaterial IdentityX25519,
    SignedPreKeyMaterial SignedPreKey,
    List<OneTimePreKeyLocal> OneTimePreKeys);
