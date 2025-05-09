namespace Ecliptix.Core.Network;

public enum RcpServiceAction : short
{
    DataCenterPubKeyExchange = 0,
    RegisterAppDeviceIfNotExist = 1,
    SendVerificationCode = 2,
    VerifyCode = 3,
    GetVerificationSessionIfExist = 4
}