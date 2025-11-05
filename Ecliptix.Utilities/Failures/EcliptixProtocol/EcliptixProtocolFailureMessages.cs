namespace Ecliptix.Utilities.Failures.EcliptixProtocol;

public static class EcliptixProtocolFailureMessages
{
    public const string INITIAL_SENDING_DH_KEY_PURPOSE = "Initial Sending DH";
    public const string PERSISTENT_DH_KEY_PURPOSE = "Persistent DH";
    public const string EPHEMERAL_DH_RATCHET_KEY_PURPOSE = "Ephemeral DH Ratchet";

    public const string UNEXPECTED_ERROR_CREATING_SESSION = "Unexpected error creating session {0}.";
    public const string SERVER_STREAMING_NOT_PERSISTED = "SERVER_STREAMING connections should not be persisted - they require fresh handshake for each session";
    public const string FAILED_TO_EXPORT_PROTO_STATE = "Failed to export connection to proto state.";
    public const string FAILED_TO_REHYDRATE_FROM_PROTO_STATE = "Failed to rehydrate connection from proto state.";
    public const string PEER_BUNDLE_NOT_SET = "Peer bundle has not been set.";
    public const string FAILED_TO_DERIVE_INITIAL_CHAIN_KEYS = "Failed to derive initial chain keys.";

    public const string RECEIVED_INDEX_TOO_LARGE = "Received index {0} is too large (potential overflow risk)";
    public const string ROOT_KEY_HANDLE_NOT_INITIALIZED = "Root key handle not initialized.";
    public const string SENDER_RATCHET_PRE_CONDITIONS_NOT_MET = "Sender ratchet pre-conditions not met.";
    public const string RECEIVER_RATCHET_PRE_CONDITIONS_NOT_MET = "Receiver ratchet pre-conditions not met.";
    public const string DH_CALCULATION_FAILED_DURING_RATCHET = "DH calculation failed during ratchet.";

    public const string NONCE_COUNTER_OVERFLOW = "Nonce counter overflow detected - connection must be rekeyed";
    public const string SESSION_EXPIRED = "Session {0} has expired.";
    public const string SENDING_CHAIN_STEP_NOT_INITIALIZED = "Sending chain step not initialized.";
    public const string RECEIVING_CHAIN_STEP_NOT_INITIALIZED = "Receiving chain step not initialized.";
    public const string SESSION_ALREADY_FINALIZED = "Session has already been finalized.";

    public const string INITIAL_ROOT_KEY_INVALID_SIZE = "Initial root key must be {0} bytes.";
    public const string INITIAL_PEER_DH_PUBLIC_KEY_INVALID_SIZE = "Initial peer DH public key must be {0} bytes.";

    public static class ChainStep
    {
        public const string KEY_WITH_INDEX_NOT_FOUND = "Key with index {0} not found.";
        public const string INITIAL_CHAIN_KEY_INVALID_SIZE = "Initial chain key must be {0} bytes.";
        public const string DH_KEYS_PROVIDED_OR_NEITHER = "Both DH private and public keys must be provided, or neither.";
        public const string INITIAL_DH_PRIVATE_KEY_INVALID_SIZE = "Initial DH private key must be {0} bytes.";
        public const string INITIAL_DH_PUBLIC_KEY_INVALID_SIZE = "Initial DH public key must be {0} bytes.";
        public const string UNEXPECTED_ERROR_PREPARING_DH_KEYS = "Unexpected error preparing DH keys.";
        public const string REQUESTED_INDEX_NOT_FUTURE = "[{0}] Requested index {1} is not future (current: {2}) and not cached.";
        public const string HKDF_FAILED_DURING_DERIVATION = "HKDF failed during derivation at index {0}.";
        public const string FAILED_TO_ALLOCATE_SECURE_MEMORY = "Failed to allocate secure memory for key {0}.";
        public const string KEY_UNEXPECTEDLY_APPEARED = "Key for index {0} unexpectedly appeared during derivation.";
        public const string DERIVED_KEY_MISSING_AFTER_LOOP = "Derived key for index {0} missing after derivation loop.";
        public const string FAILED_TO_EXPORT_CHAIN_STEP_STATE = "Failed to export chain step to proto state.";
        public const string NEW_CHAIN_KEY_INVALID_SIZE = "New chain key must be {0} bytes.";
        public const string DH_KEYS_PROVIDED_TOGETHER = "Both DH private and public keys must be provided together.";
        public const string DH_PRIVATE_KEY_INVALID_SIZE = "DH private key must be {0} bytes.";
        public const string DH_PUBLIC_KEY_INVALID_SIZE = "DH public key must be {0} bytes.";
    }

    public static class IdentityKeys
    {
        public const string FAILED_TO_READ_OPK_PRIVATE_KEY = "Failed to read OPK private key: {0}";
        public const string FAILED_TO_READ_ED_25519_SECRET_KEY = "Failed to read Ed25519 secret key: {0}";
        public const string FAILED_TO_READ_IDENTITY_X_25519_SECRET_KEY = "Failed to read Identity X25519 secret key: {0}";
        public const string FAILED_TO_READ_SIGNED_PRE_KEY_SECRET = "Failed to read Signed PreKey secret: {0}";
        public const string FAILED_TO_EXPORT_IDENTITY_KEYS_TO_PROTO = "Failed to export identity keys to proto state.";
        public const string FAILED_TO_REHYDRATE_FROM_PROTO = "Failed to rehydrate EcliptixSystemIdentityKeys from proto.";
        public const string ONE_TIME_KEY_COUNT_EXCEEDS_LIMITS = "Requested one-time key count exceeds practical limits.";
        public const string UNEXPECTED_ERROR_INITIALIZING_LOCAL_KEY_MATERIAL = "Unexpected error initializing LocalKeyMaterial: {0}";
        public const string FAILED_TO_GENERATE_ED_25519_KEY_PAIR = "Failed to generate Ed25519 key pair.";
        public const string IDENTITY_KEY_PAIR_PURPOSE = "Identity";
        public const string SIGNED_PRE_KEY_PAIR_PURPOSE = "Signed PreKey (ID: {0})";
        public const string FAILED_TO_SIGN_PRE_KEY_PUBLIC_KEY = "Failed to sign signed prekey public key.";
        public const string GENERATED_SPK_SIGNATURE_INCORRECT_SIZE = "Generated SPK signature has incorrect size ({0}).";
        public const string UNEXPECTED_ERROR_GENERATING_ONE_TIME_PREKEYS = "Unexpected error generating one-time prekeys.";
        public const string FAILED_TO_CREATE_PUBLIC_KEY_BUNDLE = "Failed to create public key bundle.";
        public const string EPHEMERAL_KEY_PAIR_PURPOSE = "Ephemeral";
        public const string UNEXPECTED_ERROR_DURING_X_3DH_DERIVATION = "An unexpected error occurred during X3DH shared secret derivation.";
        public const string UNEXPECTED_ERROR_DURING_RECIPIENT_DERIVATION = "An unexpected error occurred during Recipient shared secret derivation.";
        public const string INVALID_KEY_OR_SIGNATURE_LENGTH_FOR_SPK_VERIFICATION = "Invalid key or signature length for SPK verification.";
        public const string REMOTE_SPK_SIGNATURE_VERIFICATION_FAILED = "Remote SPK signature verification failed.";
        public const string HKDF_INFO_CANNOT_BE_EMPTY = "HKDF info cannot be empty.";
        public const string REMOTE_BUNDLE_CANNOT_BE_NULL = "Remote bundle cannot be null.";
        public const string INVALID_REMOTE_IDENTITY_X_25519_KEY = "Invalid remote IdentityX25519 key.";
        public const string INVALID_REMOTE_SIGNED_PRE_KEY_PUBLIC_KEY = "Invalid remote SignedPreKeyPublic key.";
        public const string LOCAL_EPHEMERAL_KEY_MISSING_OR_INVALID = "Local ephemeral key is missing or invalid.";
        public const string LOCAL_IDENTITY_KEY_MISSING_OR_INVALID = "Local identity key is missing or invalid.";
        public const string INVALID_REMOTE_IDENTITY_KEY_LENGTH = "Invalid remote Identity key length.";
        public const string INVALID_REMOTE_EPHEMERAL_KEY_LENGTH = "Invalid remote Ephemeral key length.";
        public const string LOCAL_OPK_HANDLE_INVALID = "Local OPK ID {0} found but its handle is invalid.";
        public const string LOCAL_OPK_NOT_FOUND = "Local OPK ID {0} not found.";
    }

    public static class ReplayProtection
    {
        public const string NONCE_CANNOT_BE_NULL_OR_EMPTY = "Nonce cannot be null or empty";
        public const string REPLAY_ATTACK_DETECTED_NONCE = "Replay attack detected: nonce already processed";
        public const string REPLAY_ATTACK_DETECTED_MESSAGE_INDEX = "Replay attack detected: message index {0} already processed for chain {1}";
        public const string MESSAGE_INDEX_TOO_FAR_BEHIND = "Message index {0} is too far behind (gap: {1}, max: {2})";
    }

    public static class PublicKeyBundle
    {
        public const string INPUT_PROTOBUF_BUNDLE_CANNOT_BE_NULL = "Input Protobuf bundle cannot be null.";
        public const string IDENTITY_ED_25519_INVALID_SIZE = "IdentityEd25519 key must be {0} bytes.";
        public const string IDENTITY_X_25519_INVALID_SIZE = "IdentityX25519 key must be {0} bytes.";
        public const string INVALID_IDENTITY_X_25519_KEY = "Invalid IdentityX25519 key: {0}";
        public const string SIGNED_PRE_KEY_PUBLIC_INVALID_SIZE = "SignedPreKeyPublic key must be {0} bytes.";
        public const string INVALID_SIGNED_PRE_KEY_PUBLIC_KEY = "Invalid SignedPreKeyPublic key: {0}";
        public const string SIGNED_PRE_KEY_SIGNATURE_INVALID_SIZE = "SignedPreKeySignature must be {0} bytes.";
        public const string EPHEMERAL_X_25519_INVALID_SIZE = "EphemeralX25519 key must be {0} bytes if present.";
        public const string INVALID_EPHEMERAL_X_25519_KEY = "Invalid EphemeralX25519 key: {0}";
        public const string INVALID_ONE_TIME_PRE_KEY = "Invalid OneTimePreKey (ID: {0}): {1}";
        public const string FAILED_TO_CREATE_FROM_PROTOBUF_INVALID_DATA = "Failed to create LocalPublicKeyBundle from Protobuf due to invalid data: {0}";
        public const string UNEXPECTED_ERROR_CREATING_FROM_PROTOBUF = "Unexpected error creating LocalPublicKeyBundle from Protobuf: {0}";
    }

    public static class DhValidator
    {
        public const string INVALID_PUBLIC_KEY_SIZE = "Invalid public key size: expected {0}, got {1}";
        public const string PUBLIC_KEY_HAS_SMALL_ORDER = "Public key has small order";
        public const string PUBLIC_KEY_NOT_VALID_CURVE_25519_POINT = "Public key is not a valid Curve25519 point";
    }
}
