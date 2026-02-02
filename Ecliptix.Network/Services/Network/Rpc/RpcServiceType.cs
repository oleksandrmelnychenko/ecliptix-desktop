namespace Ecliptix.Network.Services.Network.Rpc;

public enum RpcServiceType : short
{
    GetServerPublicKeys,
    EstablishSecrecyChannel,
    RestoreSecrecyChannel,
    EstablishAuthenticatedSecureChannel,

    RegisterAppDevice,

    ValidateMobileNumber,
    ValidateMobileForRecovery,
    CheckMobileNumberAvailability,
    GetMembershipState,

    InitiateVerification,
    VerifyOtp,

    RegistrationInit,
    RegistrationComplete,

    RecoveryInit,
    RecoveryComplete,

    SignInInitRequest,
    SignInCompleteRequest,

    Logout,
    AnonymousLogout,

    ProfileLookup,
    ProfileNameAvailability,
    ProfileUpsert
}
