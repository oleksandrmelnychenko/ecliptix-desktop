namespace Ecliptix.Utilities;

public static class ProtocolMessages
{
    public const string InvalidPublicKeySize = "Invalid public key size: expected {0}, got {1}";
    public const string PublicKeySmallOrder = "Public key has small order";
    public const string InvalidCurve25519Point = "Public key is not a valid Curve25519 point";
    
    public const string HandleDisposed = "Handle disposed";
    public const string BufferTooLarge = "Data ({0}) > buffer ({1})";
    public const string RefCountFailed = "Ref count failed";
    public const string UnexpectedWriteError = "Unexpected write error";
    public const string NegativeLengthRequested = "Negative length requested: {0}";
    public const string RequestedLengthExceedsHandle = "Requested length {0} exceeds handle length {1}";
    
    public const string NonceCounterOverflow = "Nonce counter overflow detected - connection must be rekeyed";
    
    public const string LibSodiumName = "libsodium";
    public const string Kernel32DllName = "kernel32.dll";
    public const string LibcName = "libc";
}