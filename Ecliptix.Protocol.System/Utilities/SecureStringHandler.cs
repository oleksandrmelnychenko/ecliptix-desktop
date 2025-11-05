using System.Security.Cryptography;
using System.Text;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;

namespace Ecliptix.Protocol.System.Utilities;

internal sealed class SecureStringHandler : IDisposable
{
    private readonly SodiumSecureMemoryHandle _handle;
    private readonly int _length;
    private bool _disposed;

    internal SecureStringHandler(SodiumSecureMemoryHandle handle, int length)
    {
        _handle = handle;
        _length = length;
    }

    public static Result<SecureStringHandler, SodiumFailure> FromString(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return Result<SecureStringHandler, SodiumFailure>.Err(
                SodiumFailure.InvalidBufferSize("Input string cannot be null or empty"));
        }

        byte[]? bytes = null;
        try
        {
            bytes = Encoding.UTF8.GetBytes(input);

            Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
                SodiumSecureMemoryHandle.Allocate(bytes.Length);
            if (allocResult.IsErr)
            {
                return Result<SecureStringHandler, SodiumFailure>.Err(allocResult.UnwrapErr());
            }

            SodiumSecureMemoryHandle handle = allocResult.Unwrap();
            Result<Unit, SodiumFailure> writeResult = handle.Write(bytes);
            if (!writeResult.IsErr)
            {
                return Result<SecureStringHandler, SodiumFailure>.Ok(
                    new SecureStringHandler(handle, bytes.Length));
            }

            handle.Dispose();
            return Result<SecureStringHandler, SodiumFailure>.Err(writeResult.UnwrapErr());
        }
        finally
        {
            if (bytes != null)
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }

    public Result<T, SodiumFailure> UseBytes<T>(Func<ReadOnlySpan<byte>, T> operation)
    {
        if (_disposed)
        {
            return Result<T, SodiumFailure>.Err(
                SodiumFailure.NullPointer(ProtocolSystemConstants.ErrorMessages.SECURE_STRING_HANDLER_DISPOSED));
        }

        byte[]? tempBytes = null;
        try
        {
            tempBytes = new byte[_length];
            Result<Unit, SodiumFailure> readResult = _handle.Read(tempBytes);
            if (readResult.IsErr)
            {
                return Result<T, SodiumFailure>.Err(readResult.UnwrapErr());
            }

            T result = operation(tempBytes.AsSpan(0, _length));
            return Result<T, SodiumFailure>.Ok(result);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tempBytes);
        }
    }

    public int ByteLength => _length;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle?.Dispose();
    }
}

internal sealed class SecureStringBuilder : IDisposable
{
    private readonly List<SodiumSecureMemoryHandle> _chunks;
    private readonly int _chunkSize;
    private SodiumSecureMemoryHandle? _currentChunk;
    private int _currentPosition;
    private int _totalLength;
    private bool _disposed;

    public SecureStringBuilder(
        int chunkSize = ProtocolSystemConstants.MemoryPool.SECURE_STRING_BUILDER_DEFAULT_CHUNK_SIZE)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentException(ProtocolSystemConstants.ErrorMessages.CHUNK_SIZE_POSITIVE, nameof(chunkSize));
        }

        _chunks = new List<SodiumSecureMemoryHandle>();
        _chunkSize = chunkSize;
        _currentPosition = 0;
        _totalLength = 0;
    }

    public Result<Unit, SodiumFailure> Append(char c)
    {
        if (_disposed)
        {
            return Result<Unit, SodiumFailure>.Err(
                SodiumFailure.NullPointer(ProtocolSystemConstants.ErrorMessages.SECURE_STRING_BUILDER_DISPOSED));
        }

        Span<byte> bytes = stackalloc byte[4];
        int byteCount = Encoding.UTF8.GetBytes(new[] { c }, bytes);

        return AppendBytes(bytes.Slice(0, byteCount));
    }

    public Result<Unit, SodiumFailure> Append(string str)
    {
        if (_disposed)
        {
            return Result<Unit, SodiumFailure>.Err(
                SodiumFailure.NullPointer(ProtocolSystemConstants.ErrorMessages.SECURE_STRING_BUILDER_DISPOSED));
        }

        if (string.IsNullOrEmpty(str))
        {
            return Result<Unit, SodiumFailure>.Ok(Unit.Value);
        }

        byte[]? bytes = null;
        try
        {
            bytes = Encoding.UTF8.GetBytes(str);
            return AppendBytes(bytes);
        }
        finally
        {
            if (bytes != null)
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }

    private Result<Unit, SodiumFailure> AppendBytes(ReadOnlySpan<byte> bytes)
    {
        Span<byte> singleByteBuffer = stackalloc byte[1];

        foreach (byte b in bytes)
        {
            if (_currentChunk == null || _currentPosition >= _chunkSize)
            {
                Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
                    SodiumSecureMemoryHandle.Allocate(_chunkSize);
                if (allocResult.IsErr)
                {
                    return Result<Unit, SodiumFailure>.Err(allocResult.UnwrapErr());
                }

                _currentChunk = allocResult.Unwrap();
                _chunks.Add(_currentChunk);
                _currentPosition = 0;
            }

            singleByteBuffer[0] = b;
            Result<Unit, SodiumFailure> writeResult = _currentChunk.Write(singleByteBuffer);
            if (writeResult.IsErr)
            {
                return Result<Unit, SodiumFailure>.Err(writeResult.UnwrapErr());
            }

            _currentPosition++;
            _totalLength++;
        }

        return Result<Unit, SodiumFailure>.Ok(Unit.Value);
    }

    public Result<SecureStringHandler, SodiumFailure> Build()
    {
        if (_disposed)
        {
            return Result<SecureStringHandler, SodiumFailure>.Err(
                SodiumFailure.NullPointer(ProtocolSystemConstants.ErrorMessages.SECURE_STRING_BUILDER_DISPOSED));
        }

        if (_totalLength == 0)
        {
            return Result<SecureStringHandler, SodiumFailure>.Err(
                SodiumFailure.InvalidBufferSize(ProtocolSystemConstants.ErrorMessages.SECURE_STRING_BUILDER_NO_DATA));
        }

        Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult = SodiumSecureMemoryHandle.Allocate(_totalLength);
        if (allocResult.IsErr)
        {
            return Result<SecureStringHandler, SodiumFailure>.Err(allocResult.UnwrapErr());
        }

        SodiumSecureMemoryHandle finalHandle = allocResult.Unwrap();
        byte[]? tempBuffer = null;

        try
        {
            tempBuffer = new byte[_totalLength];
            int offset = 0;

            for (int i = 0; i < _chunks.Count; i++)
            {
                SodiumSecureMemoryHandle chunk = _chunks[i];
                int bytesToRead = (i == _chunks.Count - 1) ? _currentPosition : _chunkSize;

                Result<byte[], SodiumFailure> readResult = chunk.ReadBytes(bytesToRead);
                if (readResult.IsErr)
                {
                    finalHandle.Dispose();
                    return Result<SecureStringHandler, SodiumFailure>.Err(readResult.UnwrapErr());
                }

                byte[] chunkData = readResult.Unwrap();
                Array.Copy(chunkData, 0, tempBuffer, offset, bytesToRead);
                CryptographicOperations.ZeroMemory(chunkData);
                offset += bytesToRead;
            }

            Result<Unit, SodiumFailure> writeResult = finalHandle.Write(tempBuffer);
            if (writeResult.IsErr)
            {
                finalHandle.Dispose();
                return Result<SecureStringHandler, SodiumFailure>.Err(writeResult.UnwrapErr());
            }

            return Result<SecureStringHandler, SodiumFailure>.Ok(
                new SecureStringHandler(finalHandle, _totalLength));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tempBuffer);
        }
    }

    private void Clear()
    {
        foreach (SodiumSecureMemoryHandle chunk in _chunks)
        {
            chunk.Dispose();
        }

        _chunks.Clear();
        _currentChunk = null;
        _currentPosition = 0;
        _totalLength = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clear();
    }
}
