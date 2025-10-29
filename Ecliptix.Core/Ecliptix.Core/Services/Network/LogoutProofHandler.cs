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
        uint connectId)
    {
        if (response.RevocationProof == null || response.RevocationProof.IsEmpty)
        {
            Log.Warning("[LOGOUT-PROOF] Missing revocation proof from server");
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.InvalidRevocationProof("Server did not provide revocation proof"));
        }

        byte[] revocationProof = response.RevocationProof.ToByteArray();
        const byte proofVersionHmac = 1;
        const int nonceSize = 16;
        const int hmacSize = 32;
        const int maxFingerprintSize = 64;

        if (revocationProof.Length < 1 + sizeof(int) * 2 + nonceSize + hmacSize)
        {
            Log.Warning("[LOGOUT-PROOF] Revocation proof is too small: {Size} bytes", revocationProof.Length);
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.InvalidRevocationProof($"Revocation proof too small: {revocationProof.Length} bytes"));
        }

        byte[] nonce;
        int fingerprintLength;
        byte[] fingerprint;
        byte[] hmacProof;

        try
        {
            using MemoryStream proofStream = new(revocationProof, writable: false);
            using BinaryReader reader = new(proofStream);

            byte version = reader.ReadByte();
            if (version != proofVersionHmac)
            {
                Log.Warning("[LOGOUT-PROOF] Unsupported revocation proof version: {Version}", version);
                return Result<Unit, LogoutFailure>.Err(
                    LogoutFailure.InvalidRevocationProof($"Unsupported revocation proof version: {version}"));
            }

            int nonceLength = reader.ReadInt32();
            if (nonceLength != nonceSize)
            {
                Log.Warning("[LOGOUT-PROOF] Invalid nonce length: {Length} (expected {Expected})", nonceLength,
                    nonceSize);
                return Result<Unit, LogoutFailure>.Err(
                    LogoutFailure.InvalidRevocationProof($"Invalid nonce length {nonceLength}"));
            }

            nonce = reader.ReadBytes(nonceLength);
            if (nonce.Length != nonceLength)
            {
                Log.Warning("[LOGOUT-PROOF] Unable to read nonce - expected {Expected} bytes, got {Actual}",
                    nonceLength, nonce.Length);
                return Result<Unit, LogoutFailure>.Err(
                    LogoutFailure.InvalidRevocationProof("Revocation proof truncated while reading nonce"));
            }

            fingerprintLength = reader.ReadInt32();
            if (fingerprintLength < 0 || fingerprintLength > maxFingerprintSize)
            {
                Log.Warning("[LOGOUT-PROOF] Invalid ratchet fingerprint length: {Length}", fingerprintLength);
                return Result<Unit, LogoutFailure>.Err(
                    LogoutFailure.InvalidRevocationProof($"Invalid fingerprint length {fingerprintLength}"));
            }

            fingerprint = fingerprintLength > 0 ? reader.ReadBytes(fingerprintLength) : Array.Empty<byte>();
            if (fingerprint.Length != fingerprintLength)
            {
                Log.Warning("[LOGOUT-PROOF] Unable to read fingerprint - expected {Expected} bytes, got {Actual}",
                    fingerprintLength, fingerprint.Length);
                return Result<Unit, LogoutFailure>.Err(
                    LogoutFailure.InvalidRevocationProof("Revocation proof truncated while reading fingerprint"));
            }

            int remainingBytes = (int)(revocationProof.Length - reader.BaseStream.Position);
            if (remainingBytes != hmacSize)
            {
                Log.Warning("[LOGOUT-PROOF] Unexpected HMAC length: {Length}", remainingBytes);
                return Result<Unit, LogoutFailure>.Err(
                    LogoutFailure.InvalidRevocationProof($"Invalid HMAC length {remainingBytes}"));
            }

            hmacProof = reader.ReadBytes(hmacSize);
            if (hmacProof.Length != hmacSize)
            {
                Log.Warning("[LOGOUT-PROOF] Unable to read HMAC - expected {Expected} bytes, got {Actual}",
                    hmacSize, hmacProof.Length);
                return Result<Unit, LogoutFailure>.Err(
                    LogoutFailure.InvalidRevocationProof("Revocation proof truncated while reading HMAC"));
            }
        }
        catch (EndOfStreamException)
        {
            Log.Warning("[LOGOUT-PROOF] Revocation proof truncated during parsing");
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.InvalidRevocationProof("Revocation proof truncated during parsing"));
        }

        SodiumSecureMemoryHandle? masterKeyHandle = null;
        byte[]? proofKey = null;

        try
        {
            Result<SodiumSecureMemoryHandle, AuthenticationFailure> handleResult =
                await identityService.LoadMasterKeyHandleAsync(membershipId).ConfigureAwait(false);

            if (handleResult.IsErr)
            {
                Log.Error("[LOGOUT-PROOF] Failed to load master key handle for proof verification");
                return Result<Unit, LogoutFailure>.Err(
                    LogoutFailure.CryptographicOperationFailed(
                        $"Master key retrieval failed: {handleResult.UnwrapErr().Message}"));
            }

            masterKeyHandle = handleResult.Unwrap();

            Result<byte[], SodiumFailure> proofKeyResult =
                LogoutKeyDerivation.DeriveLogoutProofKey(masterKeyHandle);

            if (proofKeyResult.IsErr)
            {
                Log.Error("[LOGOUT-PROOF] Failed to derive logout proof key");
                return Result<Unit, LogoutFailure>.Err(
                    LogoutFailure.CryptographicOperationFailed(
                        $"Proof key derivation failed: {proofKeyResult.UnwrapErr().Message}"));
            }

            proofKey = proofKeyResult.Unwrap();

            using MemoryStream ms = new();
            await using BinaryWriter writer = new(ms);

            writer.Write(Guid.Parse(membershipId).ToByteArray());
            writer.Write(connectId);
            writer.Write(response.ServerTimestamp);
            writer.Write(fingerprintLength);
            if (fingerprintLength > 0)
            {
                writer.Write(fingerprint);
            }

            writer.Write(nonce);

            writer.Flush();
            byte[] canonicalData = ms.ToArray();

            bool isValid = LogoutKeyDerivation.VerifyHmac(proofKey, canonicalData, hmacProof);

            if (!isValid)
            {
                Log.Warning("[LOGOUT-PROOF] HMAC verification failed - server proof is invalid");
                return Result<Unit, LogoutFailure>.Err(
                    LogoutFailure.InvalidRevocationProof("Server revocation proof HMAC verification failed"));
            }

            Result<Unit, LogoutFailure> storeResult =
                await StoreRevocationProofAsync(membershipId, revocationProof);

            if (storeResult.IsErr)
            {
                Log.Warning("[LOGOUT-PROOF] Failed to store revocation proof: {Error}",
                    storeResult.UnwrapErr().Message);
            }

            return Result<Unit, LogoutFailure>.Ok(Unit.Value);
        }
        finally
        {
            masterKeyHandle?.Dispose();
            if (proofKey != null)
            {
                CryptographicOperations.ZeroMemory(proofKey);
            }
        }
    }

    private async Task<Result<Unit, LogoutFailure>> StoreRevocationProofAsync(
        string membershipId,
        byte[] revocationProof)
    {
        try
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
        catch (Exception ex)
        {
            Log.Error(ex, "[LOGOUT-PROOF-STORE] Unexpected error storing revocation proof");
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError($"Unexpected error: {ex.Message}", ex));
        }
    }

    public static async Task<bool> HasRevocationProofAsync(
        IApplicationSecureStorageProvider storageProvider,
        string membershipId)
    {
        try
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

            if (proofOption.IsSome)
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LOGOUT-PROOF-CHECK] Unexpected error checking revocation proof");
            return false;
        }
    }

    public static void ClearRevocationProof(
        IApplicationSecureStorageProvider storageProvider,
        string membershipId)
    {
        try
        {
            string storageKey = GetRevocationProofStorageKey(membershipId);
            Result<Unit, InternalServiceApiFailure> deleteResult = storageProvider.Delete(storageKey);

            if (deleteResult.IsErr)
            {
                Log.Warning("[LOGOUT-PROOF-CLEAR] Failed to clear revocation proof for MembershipId: {MembershipId}",
                    membershipId);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LOGOUT-PROOF-CLEAR] Unexpected error clearing revocation proof");
        }
    }

    private static string GetRevocationProofStorageKey(string membershipId) =>
        $"{SecureStorageConstants.Identity.RevocationProofPrefix}{membershipId}";

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
