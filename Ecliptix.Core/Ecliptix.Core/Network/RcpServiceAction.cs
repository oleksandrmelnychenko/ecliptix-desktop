namespace Ecliptix.Core.Network;

public enum RcpServiceAction : short
{
    DataCenterPubKeyExchange,
    RegisterAppDevice,
    ValidatePhoneNumber,
    VerifyCode,
    InitiateVerification
}