namespace Ecliptix.Network.Services.Network.Rpc;

public enum RpcServiceType : short
{
    GetServerPublicKeys,
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
