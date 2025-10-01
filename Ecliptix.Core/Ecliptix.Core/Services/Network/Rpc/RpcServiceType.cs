namespace Ecliptix.Core.Services.Network.Rpc;

public enum RpcServiceType : short
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
    AuthenticatedEstablishSecureChannel,
}