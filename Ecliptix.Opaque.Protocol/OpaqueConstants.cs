namespace Ecliptix.Opaque.Protocol;

public static class OpaqueConstants
{
    public static readonly byte[] CredentialKeyInfo = "Ecliptix-OPAQUE-CredentialKey"u8.ToArray();
    public static readonly byte[] AkeSalt = "OPAQUE-AKE-Salt"u8.ToArray();
    public static readonly byte[] SessionKeyInfo = "session_key"u8.ToArray();
    public static readonly byte[] ClientMacKeyInfo = "client_mac_key"u8.ToArray();
    public static readonly byte[] ServerMacKeyInfo = "server_mac_key"u8.ToArray();

    public static readonly byte[] ProtocolVersion = "Ecliptix-OPAQUE-v1"u8.ToArray();

    public const int CompressedPublicKeyLength = 33;
    public const int DefaultKeyLength = 32;
    public const int MacKeyLength = 32;
    public const int ScalarSize = 32;
    public const int HashLength = 32;
    public const int NonceLength = 32;

    public const int Pbkdf2Iterations = 100000;
    public const int Pbkdf2SaltLength = 32;

    public static readonly byte[] ExportKeyInfo = "ExportKey"u8.ToArray();
    public static readonly byte[] AuthKeyInfo = "AuthKey"u8.ToArray();
    public static readonly byte[] PrivateKeyInfo = "PrivateKey"u8.ToArray();

    public const string DefaultServerIdentity = "server.ecliptix.com";

    public static class RfcCompliance
    {
        public const bool EnableOprfMasking = false;
        public const bool EnableRegistrationRecordMasking = true;
        public const bool EnableStretching = true;
        public const bool EnforcePointValidation = true;
        public const bool UseMacEnvelopes = true;
        public const bool IncludeServerIdentityInTranscript = true;
    }

    public static class ErrorMessages
    {
        public const string InvalidRegistrationRecordTooShort = "Invalid registration record: too short.";
        public const string EnvelopeMacVerificationFailed = "Envelope MAC verification failed";
        public const string ServerMacVerificationFailed = "Server MAC verification failed.";
        public const string InvalidServerStaticPublicKey = "Invalid server static public key: ";
        public const string InvalidServerEphemeralPublicKey = "Invalid server ephemeral public key: ";
        public const string PointAtInfinity = "Point is at infinity";
        public const string PointNotValid = "Point is not valid";
        public const string SubgroupCheckFailed = "Point not in main subgroup";
        public const string OprfOutputEmpty = "OPRF output cannot be empty";
        public const string EnvelopeTooShort = "Envelope too short";
        public const string MacEnvelopeCreationFailed = "MAC envelope creation failed: ";
        public const string MacVerificationFailed = "MAC verification failed: ";
        public const string ExportKeyDerivationFailed = "Export key derivation failed: ";
        public const string Pbkdf2Failed = "PBKDF2 failed: ";
        public const string InvalidOprfResponseSize = "Invalid OPRF response size: expected {0} bytes, got {1}";
        public const string InvalidClientPublicKeyLength = "Invalid client public key length: expected {0}, got {1}";
        public const string InvalidCredentialsProvided = "Invalid credentials provided.";
        public const string EllipticCurveNotFound = "Elliptic curve '{0}' not found";
        public const string FailedToInitializeEllipticCurveParameters = "Failed to initialize elliptic curve parameters: {0}";
        public const string FailedToInitializeSecureRandom = "Failed to initialize SecureRandom for cryptographic operations: {0}";
        public const string InvalidEllipticCurvePoint = "Invalid elliptic curve point";
        public const string PointNotInMainSubgroup = "Point not in main subgroup";
        public const string Pbkdf2StretchingFailed = "PBKDF2 stretching failed";
        public const string EnvelopeOperationFailed = "Envelope operation failed";
        public const string ResponseMaskingFailed = "Response masking/unmasking failed";
        public const string KeyDerivationFailed = "Key derivation failed";
        public const string OpaqueInputKeyingMaterialEmpty = "Opaque input keying material (ikm) cannot be null or empty";
        public const string OpaqueFailedToHashAfterMaxAttempts = "Opaque Failed to hash input to a valid curve point after 255 attempts.";
        public const string OpaqueDecryptionFailed = " Opaque decryption failed";
        public const string OpaqueEncryptionFailed = "Opaque encryption failed";
        public const string OpaqueMacVerificationFailed = "Opaque MAC verification failed";
    }

    public static class OperationOffsets
    {
        public const int DigestStartOffset = 0;
        public const int BlockUpdateStartOffset = 0;
        public const int MacStartOffset = 0;
    }

    public static class ProtocolIndices
    {
        public const int DhTripleCount = 3;
        public const int BigIntegerPositiveSign = 1;
        public const int DhFirstOffset = 0;
        public const int DhSecondOffset = 1;
        public const int DhThirdOffset = 2;
    }

    public static class CryptographicFlags
    {
        public const bool CompressedPointEncoding = true;
        public const bool ClearOnDispose = false;
    }

    public static class CryptographicConstants
    {
        public const string EllipticCurveName = "secp256r1";
        public const byte PointCompressionPrefix = 0x02;
        public const int MaxHashToPointAttempts = 255;
        public const int BigIntegerPositiveSign = 1;
    }

    public static class HkdfInfoStrings
    {
        public static readonly byte[] OpaqueSalt = "OPAQUE-Salt"u8.ToArray();
    }
}