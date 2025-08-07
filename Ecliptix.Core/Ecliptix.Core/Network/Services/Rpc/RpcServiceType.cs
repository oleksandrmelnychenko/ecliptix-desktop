namespace Ecliptix.Core.Network.Services.Rpc;

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
}