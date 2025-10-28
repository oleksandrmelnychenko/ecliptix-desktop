namespace Ecliptix.Core.Services.Network.Rpc;

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
}
