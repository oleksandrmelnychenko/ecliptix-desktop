using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Common;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protocol.System.Core;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using Ecliptix.Utilities.Failures.Membership;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Services.Network;

public class LogoutProofHandler(IIdentityService identityService, IApplicationSecureStorageProvider applicationSecureStorageProvider)
{

    public async Task<Result<Unit, LogoutFailure>> VerifyRevocationProofAsync(
        LogoutResponse response,
        string membershipId,
        uint connectId) =>
        await VerifyRevocationProofInternalAsync(
            identityService,
            applicationSecureStorageProvider,
            response,
            membershipId,
            connectId);

    private static async Task<Result<Unit, LogoutFailure>> VerifyRevocationProofInternalAsync(
        IIdentityService identityService,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        LogoutResponse response,
        string membershipId,
        uint connectId)
    {
        Result<byte[], LogoutFailure> proofValidation = ValidateRevocationProofFormat(response);
        if (proofValidation.IsErr)
        {
            return Result<Unit, LogoutFailure>.Err(proofValidation.UnwrapErr());
        }

        byte[] revocationProof = proofValidation.Unwrap();

        Result<ParsedProof, LogoutFailure> parseResult = ParseRevocationProof(revocationProof);
        if (parseResult.IsErr)
        {
            return Result<Unit, LogoutFailure>.Err(parseResult.UnwrapErr());
        }

        ParsedProof parsed = parseResult.Unwrap();

        return await VerifyAndStoreProofAsync(
            identityService,
            applicationSecureStorageProvider,
            membershipId,
            connectId,
            response.ServerTimestamp,
            parsed,
            revocationProof);
    }

    private static Result<byte[], LogoutFailure> ValidateRevocationProofFormat(LogoutResponse response)
    {
        if (response.RevocationProof == null || response.RevocationProof.IsEmpty)
        {
            Log.Warning("[LOGOUT-PROOF] Missing revocation proof from server");
            return Result<byte[], LogoutFailure>.Err(
                LogoutFailure.InvalidRevocationProof("Server did not provide revocation proof"));
        }

        byte[] revocationProof = response.RevocationProof.ToByteArray();
        const int NONCE_SIZE = 16;
        const int HMAC_SIZE = 32;
        int minSize = 1 + sizeof(int) * 2 + NONCE_SIZE + HMAC_SIZE;

        if (revocationProof.Length < minSize)
        {
            Log.Warning("[LOGOUT-PROOF] Revocation proof is too small: {Size} bytes", revocationProof.Length);
            return Result<byte[], LogoutFailure>.Err(
                LogoutFailure.InvalidRevocationProof($"Revocation proof too small: {revocationProof.Length} bytes"));
        }

        return Result<byte[], LogoutFailure>.Ok(revocationProof);
    }

    private readonly record struct ParsedProof(byte[] Nonce, int FingerprintLength, byte[] Fingerprint, byte[] HmacProof);

    private static Result<ParsedProof, LogoutFailure> ParseRevocationProof(byte[] revocationProof)
    {
        const byte PROOF_VERSION_HMAC = 1;
        const int NONCE_SIZE = 16;
        const int HMAC_SIZE = 32;
        const int MAX_FINGERPRINT_SIZE = 64;

        try
        {
            using MemoryStream proofStream = new(revocationProof, writable: false);
            using BinaryReader reader = new(proofStream);

            Result<Unit, LogoutFailure> versionCheck = ValidateProofVersion(reader, PROOF_VERSION_HMAC);
            if (versionCheck.IsErr)
            {
                return Result<ParsedProof, LogoutFailure>.Err(versionCheck.UnwrapErr());
            }

            Result<byte[], LogoutFailure> nonceResult = ReadNonce(reader, NONCE_SIZE);
            if (nonceResult.IsErr)
            {
                return Result<ParsedProof, LogoutFailure>.Err(nonceResult.UnwrapErr());
            }

            byte[] nonce = nonceResult.Unwrap();

            Result<FingerprintData, LogoutFailure> fingerprintResult = ReadFingerprint(reader, MAX_FINGERPRINT_SIZE);
            if (fingerprintResult.IsErr)
            {
                return Result<ParsedProof, LogoutFailure>.Err(fingerprintResult.UnwrapErr());
            }

            FingerprintData fingerprintData = fingerprintResult.Unwrap();

            Result<byte[], LogoutFailure> hmacResult = ReadHmac(reader, revocationProof.Length, HMAC_SIZE);
            if (hmacResult.IsErr)
            {
                return Result<ParsedProof, LogoutFailure>.Err(hmacResult.UnwrapErr());
            }

            byte[] hmacProof = hmacResult.Unwrap();

            return Result<ParsedProof, LogoutFailure>.Ok(new ParsedProof(nonce, fingerprintData.Length, fingerprintData.Data, hmacProof));
        }
        catch (EndOfStreamException ex)
        {
            Log.Warning(ex, "[LOGOUT-PROOF] Revocation proof truncated during parsing");
            return Result<ParsedProof, LogoutFailure>.Err(
                LogoutFailure.InvalidRevocationProof("Revocation proof truncated during parsing"));
        }
    }

    private static Result<Unit, LogoutFailure> ValidateProofVersion(BinaryReader reader, byte expectedVersion)
    {
        byte version = reader.ReadByte();
        if (version != expectedVersion)
        {
            Log.Warning("[LOGOUT-PROOF] Unsupported revocation proof version: {Version}", version);
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.InvalidRevocationProof($"Unsupported revocation proof version: {version}"));
        }
        return Result<Unit, LogoutFailure>.Ok(Unit.Value);
    }

    private static Result<byte[], LogoutFailure> ReadNonce(BinaryReader reader, int expectedSize)
    {
        int nonceLength = reader.ReadInt32();
        if (nonceLength != expectedSize)
        {
            Log.Warning("[LOGOUT-PROOF] Invalid nonce length: {Length} (expected {Expected})", nonceLength, expectedSize);
            return Result<byte[], LogoutFailure>.Err(
                LogoutFailure.InvalidRevocationProof($"Invalid nonce length {nonceLength}"));
        }

        byte[] nonce = reader.ReadBytes(nonceLength);
        if (nonce.Length != nonceLength)
        {
            Log.Warning("[LOGOUT-PROOF] Unable to read nonce - expected {Expected} bytes, got {Actual}", nonceLength, nonce.Length);
            return Result<byte[], LogoutFailure>.Err(
                LogoutFailure.InvalidRevocationProof("Revocation proof truncated while reading nonce"));
        }

        return Result<byte[], LogoutFailure>.Ok(nonce);
    }

    private readonly record struct FingerprintData(int Length, byte[] Data);

    private static Result<FingerprintData, LogoutFailure> ReadFingerprint(BinaryReader reader, int maxSize)
    {
        int fingerprintLength = reader.ReadInt32();
        if (fingerprintLength < 0 || fingerprintLength > maxSize)
        {
            Log.Warning("[LOGOUT-PROOF] Invalid ratchet fingerprint length: {Length}", fingerprintLength);
            return Result<FingerprintData, LogoutFailure>.Err(
                LogoutFailure.InvalidRevocationProof($"Invalid fingerprint length {fingerprintLength}"));
        }

        byte[] fingerprint = fingerprintLength > 0 ? reader.ReadBytes(fingerprintLength) : Array.Empty<byte>();
        if (fingerprint.Length != fingerprintLength)
        {
            Log.Warning("[LOGOUT-PROOF] Unable to read fingerprint - expected {Expected} bytes, got {Actual}", fingerprintLength, fingerprint.Length);
            return Result<FingerprintData, LogoutFailure>.Err(
                LogoutFailure.InvalidRevocationProof("Revocation proof truncated while reading fingerprint"));
        }

        return Result<FingerprintData, LogoutFailure>.Ok(new FingerprintData(fingerprintLength, fingerprint));
    }

    private static Result<byte[], LogoutFailure> ReadHmac(BinaryReader reader, int totalProofLength, int expectedHmacSize)
    {
        int remainingBytes = (int)(totalProofLength - reader.BaseStream.Position);
        if (remainingBytes != expectedHmacSize)
        {
            Log.Warning("[LOGOUT-PROOF] Unexpected HMAC length: {Length}", remainingBytes);
            return Result<byte[], LogoutFailure>.Err(
                LogoutFailure.InvalidRevocationProof($"Invalid HMAC length {remainingBytes}"));
        }

        byte[] hmacProof = reader.ReadBytes(expectedHmacSize);
        if (hmacProof.Length != expectedHmacSize)
        {
            Log.Warning("[LOGOUT-PROOF] Unable to read HMAC - expected {Expected} bytes, got {Actual}", expectedHmacSize, hmacProof.Length);
            return Result<byte[], LogoutFailure>.Err(
                LogoutFailure.InvalidRevocationProof("Revocation proof truncated while reading HMAC"));
        }

        return Result<byte[], LogoutFailure>.Ok(hmacProof);
    }

    private static async Task<Result<Unit, LogoutFailure>> VerifyAndStoreProofAsync(
        IIdentityService identityService,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        string membershipId,
        uint connectId,
        long serverTimestamp,
        ParsedProof parsed,
        byte[] revocationProof)
    {
        byte[]? proofKey = null;

        try
        {
            Result<byte[], LogoutFailure> proofKeyResult = await LoadProofKeyAsync(identityService, membershipId);
            if (proofKeyResult.IsErr)
            {
                return Result<Unit, LogoutFailure>.Err(proofKeyResult.UnwrapErr());
            }

            proofKey = proofKeyResult.Unwrap();

            byte[] canonicalData = BuildCanonicalData(membershipId, connectId, serverTimestamp, parsed);

            bool isValid = LogoutKeyDerivation.VerifyHmac(proofKey, canonicalData, parsed.HmacProof);
            if (!isValid)
            {
                Log.Warning("[LOGOUT-PROOF] HMAC verification failed - server proof is invalid");
                return Result<Unit, LogoutFailure>.Err(
                    LogoutFailure.InvalidRevocationProof("Server revocation proof HMAC verification failed"));
            }

            Result<Unit, LogoutFailure> storeResult = await StoreRevocationProofAsync(applicationSecureStorageProvider, membershipId, revocationProof);
            if (storeResult.IsErr)
            {
                Log.Warning("[LOGOUT-PROOF] Failed to store revocation proof: {ERROR}", storeResult.UnwrapErr().Message);
            }

            return Result<Unit, LogoutFailure>.Ok(Unit.Value);
        }
        finally
        {
            if (proofKey != null)
            {
                CryptographicOperations.ZeroMemory(proofKey);
            }
        }
    }

    private static async Task<Result<byte[], LogoutFailure>> LoadProofKeyAsync(
        IIdentityService identityService,
        string membershipId)
    {
        Result<SodiumSecureMemoryHandle, AuthenticationFailure> handleResult =
            await identityService.LoadMasterKeyHandleAsync(membershipId).ConfigureAwait(false);

        if (handleResult.IsErr)
        {
            Log.Error("[LOGOUT-PROOF] Failed to load master key handle for proof verification");
            return Result<byte[], LogoutFailure>.Err(
                LogoutFailure.CryptographicOperationFailed(
                    $"Master key retrieval failed: {handleResult.UnwrapErr().Message}"));
        }

        using SodiumSecureMemoryHandle masterKeyHandle = handleResult.Unwrap();

        Result<byte[], SodiumFailure> proofKeyResult = LogoutKeyDerivation.DeriveLogoutProofKey(masterKeyHandle);
        if (proofKeyResult.IsErr)
        {
            Log.Error("[LOGOUT-PROOF] Failed to derive logout proof key");
            return Result<byte[], LogoutFailure>.Err(
                LogoutFailure.CryptographicOperationFailed(
                    $"Proof key derivation failed: {proofKeyResult.UnwrapErr().Message}"));
        }

        return Result<byte[], LogoutFailure>.Ok(proofKeyResult.Unwrap());
    }

    private static byte[] BuildCanonicalData(string membershipId, uint connectId, long serverTimestamp, ParsedProof parsed)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(Guid.Parse(membershipId).ToByteArray());
        writer.Write(connectId);
        writer.Write(serverTimestamp);
        writer.Write(parsed.FingerprintLength);
        if (parsed.FingerprintLength > 0)
        {
            writer.Write(parsed.Fingerprint);
        }
        writer.Write(parsed.Nonce);
        writer.Flush();

        return ms.ToArray();
    }

    private static async Task<Result<Unit, LogoutFailure>> StoreRevocationProofAsync(
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        string membershipId,
        byte[] revocationProof)
    {
        string storageKey = GetRevocationProofStorageKey(membershipId);

        Result<Unit, InternalServiceApiFailure> storeResult =
            await applicationSecureStorageProvider.StoreAsync(storageKey, revocationProof)
                .ConfigureAwait(false);

        if (storeResult.IsErr)
        {
            Log.Error("[LOGOUT-PROOF-STORE] Failed to store revocation proof for MembershipId: {MembershipId}",
                membershipId);
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError(
                    $"Revocation proof storage failed: {storeResult.UnwrapErr().Message}"));
        }

        return Result<Unit, LogoutFailure>.Ok(Unit.Value);
    }

    public static async Task<bool> HasRevocationProofAsync(
        IApplicationSecureStorageProvider storageProvider,
        string membershipId)
    {
        string storageKey = GetRevocationProofStorageKey(membershipId);

        Result<Option<byte[]>, InternalServiceApiFailure> getResult =
            await storageProvider.TryGetByKeyAsync(storageKey).ConfigureAwait(false);

        if (getResult.IsErr)
        {
            Log.Warning("[LOGOUT-PROOF-CHECK] Failed to check revocation proof for MembershipId: {MembershipId}",
                membershipId);
            return false;
        }

        Option<byte[]> proofOption = getResult.Unwrap();

        return proofOption.IsSome;
    }

    public static void ClearRevocationProof(
        IApplicationSecureStorageProvider storageProvider,
        string membershipId)
    {
        string storageKey = GetRevocationProofStorageKey(membershipId);
        Result<Unit, InternalServiceApiFailure> deleteResult = storageProvider.Delete(storageKey);

        if (deleteResult.IsErr)
        {
            Log.Warning("[LOGOUT-PROOF-CLEAR] Failed to clear revocation proof for MembershipId: {MembershipId}",
                membershipId);
        }
    }

    private static string GetRevocationProofStorageKey(string membershipId) =>
        $"{SecureStorageConstants.Identity.REVOCATION_PROOF_PREFIX}{membershipId}";

    public async Task<Result<Unit, LogoutFailure>> GenerateLogoutHmacProofAsync(
        LogoutRequest request,
        string membershipId)
    {
        SodiumSecureMemoryHandle? masterKeyHandle = null;
        byte[]? hmacKey = null;

        try
        {
            Result<SodiumSecureMemoryHandle, AuthenticationFailure> handleResult =
                await identityService.LoadMasterKeyHandleAsync(membershipId).ConfigureAwait(false);

            if (handleResult.IsErr)
            {
                Log.Error("[LOGOUT-HMAC] Failed to load master key handle for MembershipId: {MembershipId}",
                    membershipId);
                return Result<Unit, LogoutFailure>.Err(
                    LogoutFailure.CryptographicOperationFailed(
                        $"Master key retrieval failed: {handleResult.UnwrapErr().Message}"));
            }

            masterKeyHandle = handleResult.Unwrap();

            Result<byte[], SodiumFailure> hmacKeyResult =
                LogoutKeyDerivation.DeriveLogoutHmacKey(masterKeyHandle);

            if (hmacKeyResult.IsErr)
            {
                Log.Error("[LOGOUT-HMAC] Failed to derive logout HMAC key for MembershipId: {MembershipId}",
                    membershipId);
                return Result<Unit, LogoutFailure>.Err(
                    LogoutFailure.CryptographicOperationFailed(
                        $"HMAC key derivation failed: {hmacKeyResult.UnwrapErr().Message}"));
            }

            hmacKey = hmacKeyResult.Unwrap();

            string canonical = $"logout:v1:{request.MembershipIdentifier.ToBase64()}:" +
                               $"{request.Timestamp}:{request.Scope}:{request.LogoutReason}";
            byte[] canonicalBytes = Encoding.UTF8.GetBytes(canonical);

            byte[] hmacProof = LogoutKeyDerivation.ComputeHmac(hmacKey, canonicalBytes);
            request.HmacProof = ByteString.CopyFrom(hmacProof);

            return Result<Unit, LogoutFailure>.Ok(Unit.Value);
        }
        finally
        {
            masterKeyHandle?.Dispose();
            if (hmacKey != null)
            {
                CryptographicOperations.ZeroMemory(hmacKey);
            }
        }
    }

}
