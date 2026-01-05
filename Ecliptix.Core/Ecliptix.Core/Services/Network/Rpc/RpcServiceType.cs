namespace Ecliptix.Core.Services.Network.Rpc;

// Legacy logical service identifiers used internally by the client.
// These are now mapped to gateway event types.
public enum RpcServiceType : short
{
    EstablishSecrecyChannel,
    RestoreSecrecyChannel,
    EstablishAuthenticatedSecureChannel,

    RegisterAppDevice,

    ValidateMobileNumber,
    CheckMobileNumberAvailability,

    InitiateVerification,
    VerifyOtp,

    RegistrationInit,
    RegistrationComplete,

    RecoverySecretKeyInit,
    RecoverySecretKeyComplete,

    SignInInitRequest,
    SignInCompleteRequest,

    Logout,
    AnonymousLogout,

    GetAccountProfile,
    CheckProfileNameAvailability,
    CreateOrUpdateProfile
}
