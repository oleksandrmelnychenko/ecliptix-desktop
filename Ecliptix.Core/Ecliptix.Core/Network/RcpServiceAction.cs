namespace Ecliptix.Core.Network;

public enum RcpServiceAction : short
{
    DataCenterPubKeyExchange,
    RegisterAppDevice,
    ValidatePhoneNumber,
    VerifyOtp,
    InitiateVerification,
    SignIn,
    UpdateMembershipWithSecureKey
}