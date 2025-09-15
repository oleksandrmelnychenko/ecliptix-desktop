namespace Ecliptix.Utilities.Failures.EcliptixProtocol;

public static class EcliptixProtocolFailureMessages
{
    public const string InitialSendingDhKeyPurpose = "Initial Sending DH";
    public const string PersistentDhKeyPurpose = "Persistent DH";
    public const string EphemeralDhRatchetKeyPurpose = "Ephemeral DH Ratchet";

    public const string UnexpectedErrorCreatingSession = "Unexpected error creating session {0}.";
    public const string ServerStreamingNotPersisted = "SERVER_STREAMING connections should not be persisted - they require fresh handshake for each session";
    public const string FailedToExportProtoState = "Failed to export connection to proto state.";
    public const string FailedToRehydrateFromProtoState = "Failed to rehydrate connection from proto state.";
    public const string PeerBundleNotSet = "Peer bundle has not been set.";
    public const string FailedToDeriveInitialChainKeys = "Failed to derive initial chain keys.";

    public const string ReceivedIndexTooLarge = "Received index {0} is too large (potential overflow risk)";
    public const string RootKeyHandleNotInitialized = "Root key handle not initialized.";
    public const string SenderRatchetPreConditionsNotMet = "Sender ratchet pre-conditions not met.";
    public const string ReceiverRatchetPreConditionsNotMet = "Receiver ratchet pre-conditions not met.";
    public const string DhCalculationFailedDuringRatchet = "DH calculation failed during ratchet.";

    public const string NonceCounterOverflow = "Nonce counter overflow detected - connection must be rekeyed";
    public const string SessionExpired = "Session {0} has expired.";
    public const string SendingChainStepNotInitialized = "Sending chain step not initialized.";
    public const string ReceivingChainStepNotInitialized = "Receiving chain step not initialized.";
    public const string SessionAlreadyFinalized = "Session has already been finalized.";

    public const string InitialRootKeyInvalidSize = "Initial root key must be {0} bytes.";
    public const string InitialPeerDhPublicKeyInvalidSize = "Initial peer DH public key must be {0} bytes.";

    public static class ChainStep
    {
        public const string KeyWithIndexNotFound = "Key with index {0} not found.";
        public const string InitialChainKeyInvalidSize = "Initial chain key must be {0} bytes.";
        public const string DhKeysProvidedOrNeither = "Both DH private and public keys must be provided, or neither.";
        public const string InitialDhPrivateKeyInvalidSize = "Initial DH private key must be {0} bytes.";
        public const string InitialDhPublicKeyInvalidSize = "Initial DH public key must be {0} bytes.";
        public const string UnexpectedErrorPreparingDhKeys = "Unexpected error preparing DH keys.";
        public const string RequestedIndexNotFuture = "[{0}] Requested index {1} is not future (current: {2}) and not cached.";
        public const string HkdfFailedDuringDerivation = "HKDF failed during derivation at index {0}.";
        public const string FailedToAllocateSecureMemory = "Failed to allocate secure memory for key {0}.";
        public const string KeyUnexpectedlyAppeared = "Key for index {0} unexpectedly appeared during derivation.";
        public const string DerivedKeyMissingAfterLoop = "Derived key for index {0} missing after derivation loop.";
        public const string FailedToExportChainStepState = "Failed to export chain step to proto state.";
        public const string NewChainKeyInvalidSize = "New chain key must be {0} bytes.";
        public const string DhKeysProvidedTogether = "Both DH private and public keys must be provided together.";
        public const string DhPrivateKeyInvalidSize = "DH private key must be {0} bytes.";
        public const string DhPublicKeyInvalidSize = "DH public key must be {0} bytes.";
    }

    public static class IdentityKeys
    {
        public const string FailedToReadOpkPrivateKey = "Failed to read OPK private key: {0}";
        public const string FailedToReadEd25519SecretKey = "Failed to read Ed25519 secret key: {0}";
        public const string FailedToReadIdentityX25519SecretKey = "Failed to read Identity X25519 secret key: {0}";
        public const string FailedToReadSignedPreKeySecret = "Failed to read Signed PreKey secret: {0}";
        public const string FailedToExportIdentityKeysToProto = "Failed to export identity keys to proto state.";
        public const string FailedToRehydrateFromProto = "Failed to rehydrate EcliptixSystemIdentityKeys from proto.";
        public const string OneTimeKeyCountExceedsLimits = "Requested one-time key count exceeds practical limits.";
        public const string UnexpectedErrorInitializingLocalKeyMaterial = "Unexpected error initializing LocalKeyMaterial: {0}";
        public const string FailedToGenerateEd25519KeyPair = "Failed to generate Ed25519 key pair.";
        public const string IdentityKeyPairPurpose = "Identity";
        public const string SignedPreKeyPairPurpose = "Signed PreKey (ID: {0})";
        public const string FailedToSignPreKeyPublicKey = "Failed to sign signed prekey public key.";
        public const string GeneratedSpkSignatureIncorrectSize = "Generated SPK signature has incorrect size ({0}).";
        public const string UnexpectedErrorGeneratingOneTimePrekeys = "Unexpected error generating one-time prekeys.";
        public const string FailedToCreatePublicKeyBundle = "Failed to create public key bundle.";
        public const string EphemeralKeyPairPurpose = "Ephemeral";
        public const string UnexpectedErrorDuringX3dhDerivation = "An unexpected error occurred during X3DH shared secret derivation.";
        public const string UnexpectedErrorDuringRecipientDerivation = "An unexpected error occurred during Recipient shared secret derivation.";
        public const string InvalidKeyOrSignatureLengthForSpkVerification = "Invalid key or signature length for SPK verification.";
        public const string RemoteSpkSignatureVerificationFailed = "Remote SPK signature verification failed.";
        public const string HkdfInfoCannotBeEmpty = "HKDF info cannot be empty.";
        public const string RemoteBundleCannotBeNull = "Remote bundle cannot be null.";
        public const string InvalidRemoteIdentityX25519Key = "Invalid remote IdentityX25519 key.";
        public const string InvalidRemoteSignedPreKeyPublicKey = "Invalid remote SignedPreKeyPublic key.";
        public const string LocalEphemeralKeyMissingOrInvalid = "Local ephemeral key is missing or invalid.";
        public const string LocalIdentityKeyMissingOrInvalid = "Local identity key is missing or invalid.";
        public const string InvalidRemoteIdentityKeyLength = "Invalid remote Identity key length.";
        public const string InvalidRemoteEphemeralKeyLength = "Invalid remote Ephemeral key length.";
        public const string LocalOpkHandleInvalid = "Local OPK ID {0} found but its handle is invalid.";
        public const string LocalOpkNotFound = "Local OPK ID {0} not found.";
    }

    public static class AdaptiveRatchet
    {
        public const string FailedToSerializeState = "Failed to serialize AdaptiveRatchetManager state";
        public const string FailedToDeserializeState = "Failed to deserialize AdaptiveRatchetManager state";
    }

    public static class ReplayProtection
    {
        public const string NonceCannotBeNullOrEmpty = "Nonce cannot be null or empty";
        public const string ReplayAttackDetectedNonce = "Replay attack detected: nonce already processed";
        public const string ReplayAttackDetectedMessageIndex = "Replay attack detected: message index {0} already processed for chain {1}";
        public const string MessageIndexTooFarBehind = "Message index {0} is too far behind (gap: {1}, max: {2})";
    }

    public static class CircuitBreaker
    {
        public const string CircuitBreakerDisposed = "Circuit breaker has been disposed";
        public const string OperationFailedInCircuitBreaker = "Operation failed in circuit breaker";
        public const string CircuitBreakerIsOpen = "Circuit breaker is OPEN. Blocking requests until {0:HH:mm:ss}";
        public const string CircuitBreakerHalfOpenTestingLimitReached = "Circuit breaker is HALF-OPEN but testing limit reached";
        public const string UnknownCircuitBreakerState = "Unknown circuit breaker state: {0}";
    }

    public static class PublicKeyBundle
    {
        public const string InputProtobufBundleCannotBeNull = "Input Protobuf bundle cannot be null.";
        public const string IdentityEd25519InvalidSize = "IdentityEd25519 key must be {0} bytes.";
        public const string IdentityX25519InvalidSize = "IdentityX25519 key must be {0} bytes.";
        public const string InvalidIdentityX25519Key = "Invalid IdentityX25519 key: {0}";
        public const string SignedPreKeyPublicInvalidSize = "SignedPreKeyPublic key must be {0} bytes.";
        public const string InvalidSignedPreKeyPublicKey = "Invalid SignedPreKeyPublic key: {0}";
        public const string SignedPreKeySignatureInvalidSize = "SignedPreKeySignature must be {0} bytes.";
        public const string EphemeralX25519InvalidSize = "EphemeralX25519 key must be {0} bytes if present.";
        public const string InvalidEphemeralX25519Key = "Invalid EphemeralX25519 key: {0}";
        public const string InvalidOneTimePreKey = "Invalid OneTimePreKey (ID: {0}): {1}";
        public const string FailedToCreateFromProtobufInvalidData = "Failed to create LocalPublicKeyBundle from Protobuf due to invalid data: {0}";
        public const string UnexpectedErrorCreatingFromProtobuf = "Unexpected error creating LocalPublicKeyBundle from Protobuf: {0}";
    }

    public static class DhValidator
    {
        public const string InvalidPublicKeySize = "Invalid public key size: expected {0}, got {1}";
        public const string PublicKeyHasSmallOrder = "Public key has small order";
        public const string PublicKeyNotValidCurve25519Point = "Public key is not a valid Curve25519 point";
    }

    public static class OperationNames
    {
        public const string PrepareNextSendMessage = "PrepareNextSendMessage";
        public const string ProcessReceivedMessage = "ProcessReceivedMessage";
        public const string DhRatchet = "DH-Ratchet";
        public const string GenerateNonce = "GenerateNonce";
    }
}