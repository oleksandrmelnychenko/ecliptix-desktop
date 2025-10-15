namespace Ecliptix.Utilities;

internal static class Constants
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

    public static readonly byte[] MsgInfo = { UtilityConstants.ProtocolBytes.MsgInfoValue };
    public static readonly byte[] ChainInfo = { UtilityConstants.ProtocolBytes.ChainInfoValue };

    public static ReadOnlySpan<byte> X3dhInfo => System.Text.Encoding.UTF8.GetBytes(UtilityConstants.ProtocolNames.X3dhInfo);

    public const int Curve25519FieldElementSize = 32;
    public const int WordSize = 4;
    public const int Field256WordCount = 8;
    public const uint FieldElementMask = 0x7FFFFFFF;
    public const int SmallBufferThreshold = 64;

    public const int UInt32LittleEndianOffset = 8;
}
