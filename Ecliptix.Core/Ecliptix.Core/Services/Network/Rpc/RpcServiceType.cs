namespace Ecliptix.Core.Services.Network.Rpc;

public enum RpcServiceType : short
{
    EstablishSecrecyChannel,
    RestoreSecrecyChannel,
    RegisterAppDevice,
    ValidateMobileNumber,
    VerifyOtp,
    InitiateVerification,
    OpaqueRegistrationInit,
    OpaqueRegistrationComplete,
    OpaqueRecoverySecretKeyInit,
    OpaqueRecoverySecretKeyComplete,
    OpaqueSignInInitRequest,
    OpaqueSignInCompleteRequest,
    AuthenticatedEstablishSecureChannel,
    Logout,
}
