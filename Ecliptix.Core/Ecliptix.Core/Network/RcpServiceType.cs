namespace Ecliptix.Core.Network;

public enum RcpServiceType : short
{
    EstablishSecrecyChannel,
    RestoreSecrecyChannel,
    RegisterAppDevice,
    ValidatePhoneNumber,
    VerifyOtp,
    InitiateVerification,
    OpaqueRegistrationInit,
    OpaqueRegistrationComplete,
    OpaqueSignInInitRequest,
    OpaqueSignInCompleteRequest,
}