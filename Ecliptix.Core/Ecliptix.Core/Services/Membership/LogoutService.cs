using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Models.Membership;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Membership;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Core;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using Ecliptix.Utilities.Failures.Membership;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Services.Membership;

internal sealed class LogoutService(
    NetworkProvider networkProvider,
    IMessageBus messageBus,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    IApplicationStateManager stateManager,
    IStateCleanupService stateCleanupService,
    IApplicationRouter router,
    IIdentityService identityService)
    : ILogoutService
{
    public async Task<Result<Unit, LogoutFailure>> LogoutAsync(LogoutReason reason,
        CancellationToken cancellationToken = default)
    {
        Option<string> membershipIdOption = await GetCurrentMembershipIdAsync().ConfigureAwait(false);
        if (!membershipIdOption.HasValue)
        {
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.InvalidMembershipIdentifier("No active session found"));
        }

        string membershipId = membershipIdOption.Value!;

        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync().ConfigureAwait(false);

        if (settingsResult.IsErr)
        {
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.NetworkRequestFailed("Failed to get application settings",
                    new Exception(settingsResult.UnwrapErr().Message)));
        }

        ApplicationInstanceSettings settings = settingsResult.Unwrap();

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ByteString membershipIdBytes = Helpers.GuidToByteString(Guid.Parse(membershipId));

        LogoutRequest logoutRequest = new()
        {
            MembershipIdentifier = membershipIdBytes,
            LogoutReason = reason.ToString(),
            Timestamp = timestamp,
            Scope = LogoutScope.ThisDevice
        };

        Result<Unit, LogoutFailure> hmacResult = await GenerateLogoutHmacProofAsync(logoutRequest, membershipId);
        if (hmacResult.IsErr)
        {
            Log.Warning("[LOGOUT] Failed to generate HMAC proof: {Error}", hmacResult.UnwrapErr().Message);
            return hmacResult;
        }

        uint connectId = NetworkProvider.ComputeUniqueConnectId(
            settings,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        Log.Information("[LOGOUT] Using existing authenticated protocol. ConnectId: {ConnectId}", connectId);

        TaskCompletionSource<Result<LogoutResponse, LogoutFailure>> responseCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.Logout,
            logoutRequest.ToByteArray(),
            responsePayload =>
            {
                try
                {
                    LogoutResponse logoutResponse = LogoutResponse.Parser.ParseFrom(responsePayload);

                    if (logoutResponse.Result != LogoutResponse.Types.Result.Succeeded)
                    {
                        Log.Warning("[LOGOUT] Server returned non-success status: {Status}", logoutResponse.Result);
                    }

                    responseCompletionSource.TrySetResult(MapLogoutResponse(logoutResponse));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[LOGOUT] Failed to parse logout response");
                    responseCompletionSource.TrySetResult(Result<LogoutResponse, LogoutFailure>.Err(
                        LogoutFailure.UnexpectedError("Failed to parse logout response", ex)));
                }

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            },
            allowDuplicates: false,
            token: cancellationToken).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            NetworkFailure failure = networkResult.UnwrapErr();
            Log.Warning("[LOGOUT] Network request failed: {Error}", failure.Message);
            return Result<Unit, LogoutFailure>.Err(MapNetworkFailure(failure));
        }

        Result<LogoutResponse, LogoutFailure>
            responseResult = await responseCompletionSource.Task.ConfigureAwait(false);

        if (responseResult.IsErr)
        {
            return Result<Unit, LogoutFailure>.Err(responseResult.UnwrapErr());
        }

        LogoutResponse response = responseResult.Unwrap();

        if (response.Result == LogoutResponse.Types.Result.Succeeded)
        {
            Result<Unit, LogoutFailure> proofVerification =
                await VerifyRevocationProofAsync(response, membershipId, connectId);

            if (proofVerification.IsErr)
            {
                Log.Error("[LOGOUT] Revocation proof verification failed for MembershipId: {MembershipId}",
                    membershipId);
                return proofVerification;
            }
        }

        Log.Information("[LOGOUT] Logout API call succeeded. Starting cleanup for MembershipId: {MembershipId}",
            membershipId);

        Result<Unit, Exception> cleanupResult =
            await stateCleanupService.CleanupMembershipStateAsync(membershipId, connectId).ConfigureAwait(false);
        if (cleanupResult.IsErr)
        {
            Log.Warning(
                "[LOGOUT-CLEANUP] Failed to cleanup user state during logout. MembershipId: {MembershipId}, Error: {Error}",
                membershipId, cleanupResult.UnwrapErr().Message);
        }

        await stateManager.TransitionToAnonymousAsync().ConfigureAwait(false);

        await messageBus.PublishAsync(new MembershipLoggedOutEvent(membershipId, reason.ToString()), cancellationToken)
            .ConfigureAwait(false);

        await router.NavigateToAuthenticationAsync().ConfigureAwait(false);

        Log.Information("[LOGOUT] Logout completed successfully. MembershipId: {MembershipId}", membershipId);

        return Result<Unit, LogoutFailure>.Ok(Unit.Value);
    }

    private static Result<LogoutResponse, LogoutFailure> MapLogoutResponse(LogoutResponse response)
    {
        return response.Result switch
        {
            LogoutResponse.Types.Result.Succeeded => Result<LogoutResponse, LogoutFailure>.Ok(response),
            LogoutResponse.Types.Result.AlreadyLoggedOut => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.AlreadyLoggedOut("Session is already logged out on the server")),
            LogoutResponse.Types.Result.SessionNotFound => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.SessionNotFound("Active session was not found on the server")),
            LogoutResponse.Types.Result.InvalidTimestamp => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError("Server rejected logout due to timestamp mismatch")),
            LogoutResponse.Types.Result.InvalidHmac => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.CryptographicOperationFailed("Server rejected logout due to invalid HMAC")),
            LogoutResponse.Types.Result.Failed => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError("Server failed to complete logout")),
            _ => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError("Server returned unknown logout status"))
        };
    }

    private static LogoutFailure MapNetworkFailure(NetworkFailure failure)
    {
        string message = failure.UserError?.Message ?? failure.Message;
        return LogoutFailure.NetworkRequestFailed(message, failure.InnerException);
    }

    private async Task<Option<string>> GetCurrentMembershipIdAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync().ConfigureAwait(false);

        if (settingsResult.IsErr)
            return Option<string>.None;

        ApplicationInstanceSettings settings = settingsResult.Unwrap();

        return settings.Membership?.UniqueIdentifier == null
            ? Option<string>.None
            : Option<string>.Some(Helpers.FromByteStringToGuid(settings.Membership.UniqueIdentifier).ToString());
    }

    private async Task<Result<Unit, LogoutFailure>> GenerateLogoutHmacProofAsync(
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

            Log.Information("[LOGOUT-HMAC] HMAC proof generated for MembershipId: {MembershipId}", membershipId);

            return Result<Unit, LogoutFailure>.Ok(Unit.Value);
        }
        finally
        {
            masterKeyHandle?.Dispose();
            if (hmacKey != null)
                CryptographicOperations.ZeroMemory(hmacKey);
        }
    }

    private async Task<Result<Unit, LogoutFailure>> VerifyRevocationProofAsync(
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
                writer.Write(fingerprint);
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

            Log.Information("[LOGOUT-PROOF] Revocation proof verified successfully for MembershipId: {MembershipId}",
                membershipId);

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
                CryptographicOperations.ZeroMemory(proofKey);
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

            Log.Information("[LOGOUT-PROOF-STORE] Revocation proof stored for MembershipId: {MembershipId}",
                membershipId);

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

            if (proofOption.HasValue)
            {
                Log.Information(
                    "[LOGOUT-PROOF-CHECK] Revocation proof found for MembershipId: {MembershipId} - session was logged out",
                    membershipId);
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
            else
            {
                Log.Information("[LOGOUT-PROOF-CLEAR] Revocation proof cleared for MembershipId: {MembershipId}",
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
}
