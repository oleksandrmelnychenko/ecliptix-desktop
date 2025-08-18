namespace Ecliptix.Utilities;

public static class Constants
{
    public const int X25519KeySize = 32;
    public const int Ed25519KeySize = 32;

    public const int Ed25519PublicKeySize = 32;
    public const int Ed25519SecretKeySize = 64;
    public const int Ed25519SignatureSize = 64;
    public const int X25519PublicKeySize = 32;
    public const int X25519PrivateKeySize = 32;
    public const int AesKeySize = 32;
    public const int AesGcmNonceSize = 12;
    public const int AesGcmTagSize = 16;

    public static readonly byte[] MsgInfo = { 0x01 };
    public static readonly byte[] ChainInfo = { 0x02 };

    public static readonly byte[] DhRatchetInfo = { 0x03 };

    public static readonly byte[] InitialSenderChainInfo = { 0x11 };
    public static readonly byte[] InitialReceiverChainInfo = { 0x12 };
    public static ReadOnlySpan<byte> X3dhInfo => "Ecliptix_X3DH"u8;

    public const int Curve25519FieldElementSize = 32;
    public const int WordSize = 4;
    public const int Field256WordCount = 8;
    public const uint FieldElementMask = 0x7FFFFFFF;
    public const int SmallBufferThreshold = 64;

    public const int UInt32LittleEndianOffset = 8;
}