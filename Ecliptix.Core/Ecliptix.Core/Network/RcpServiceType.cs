namespace Ecliptix.Core.Network;

public enum RcpServiceType : short
{
    EstablishSecrecyChannel,
    RestoreSecrecyChannelState,
    RegisterAppDevice,
    ValidatePhoneNumber,
    VerifyOtp,
    InitiateVerification,
    OpaqueRegistrationInit,
    OpaqueRegistrationComplete,
    OpaqueSignInInitRequest,
    OpaqueSignInCompleteRequest,
}