using Ecliptix.Protobuf.Transport.Common;
using Ecliptix.Protobuf.Transport.DeviceProvisioning;
using Ecliptix.Protobuf.Transport.Identity;

namespace Ecliptix.Network.Services.Network.Rpc;

internal static class GatewayRouteCatalog
{
    private static readonly Dictionary<RpcServiceType, GatewayRoute> Routes = new()
    {
        { RpcServiceType.GetServerPublicKeys, new GatewayRoute(EventContext.DeviceProvisioning, DeviceProvisioningEventType.DeviceProvisioningGetServerPublicKeys.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.RegisterAppDevice, new GatewayRoute(EventContext.DeviceProvisioning, DeviceProvisioningEventType.DeviceProvisioningRegisterDevice.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.EstablishSecrecyChannel, new GatewayRoute(EventContext.DeviceProvisioning, DeviceProvisioningEventType.DeviceProvisioningSecureChannelEstablish.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.RestoreSecrecyChannel, new GatewayRoute(EventContext.DeviceProvisioning, DeviceProvisioningEventType.DeviceProvisioningSecureChannelRestore.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.EstablishAuthenticatedSecureChannel, new GatewayRoute(EventContext.DeviceProvisioning, DeviceProvisioningEventType.DeviceProvisioningSecureChannelAuthEstablish.ToString(), DeliveryKind.Unary) },

        { RpcServiceType.ValidateMobileNumber, new GatewayRoute(EventContext.IdentityAccess, IdentityAccessEventType.IdentityAccessValidateMobileNumber.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.CheckMobileNumberAvailability, new GatewayRoute(EventContext.IdentityAccess, IdentityAccessEventType.IdentityAccessCheckMobileAvailability.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.InitiateVerification, new GatewayRoute(EventContext.IdentityAccess, IdentityAccessEventType.IdentityAccessRecoveryMobileVerification.ToString(), DeliveryKind.ServerStream) },
        { RpcServiceType.VerifyOtp, new GatewayRoute(EventContext.IdentityAccess, IdentityAccessEventType.IdentityAccessVerifyOtp.ToString(), DeliveryKind.Unary) },

        { RpcServiceType.RegistrationInit, new GatewayRoute(EventContext.IdentityAccess, IdentityAccessEventType.IdentityAccessRegistrationInit.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.RegistrationComplete, new GatewayRoute(EventContext.IdentityAccess, IdentityAccessEventType.IdentityAccessRegistrationComplete.ToString(), DeliveryKind.Unary) },

        { RpcServiceType.RecoverySecretKeyInit, new GatewayRoute(EventContext.IdentityAccess, IdentityAccessEventType.IdentityAccessRecoveryInit.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.RecoverySecretKeyComplete, new GatewayRoute(EventContext.IdentityAccess, IdentityAccessEventType.IdentityAccessRecoveryComplete.ToString(), DeliveryKind.Unary) },

        { RpcServiceType.SignInInitRequest, new GatewayRoute(EventContext.IdentityAccess, IdentityAccessEventType.IdentityAccessSignInInit.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.SignInCompleteRequest, new GatewayRoute(EventContext.IdentityAccess, IdentityAccessEventType.IdentityAccessSignInComplete.ToString(), DeliveryKind.Unary) },

        { RpcServiceType.Logout, new GatewayRoute(EventContext.IdentityAccess, IdentityAccessEventType.IdentityAccessLogout.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.AnonymousLogout, new GatewayRoute(EventContext.IdentityAccess, IdentityAccessEventType.IdentityAccessLogoutAnonymous.ToString(), DeliveryKind.Unary) },

        { RpcServiceType.GetAccountProfile, new GatewayRoute(EventContext.IdentityAccess, IdentityAccessEventType.IdentityAccessGetProfile.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.CheckProfileNameAvailability, new GatewayRoute(EventContext.IdentityAccess, IdentityAccessEventType.IdentityAccessCheckProfileName.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.CreateOrUpdateProfile, new GatewayRoute(EventContext.IdentityAccess, IdentityAccessEventType.IdentityAccessUpsertProfile.ToString(), DeliveryKind.Unary) }
    };

    public static bool TryGetRoute(RpcServiceType serviceType, out GatewayRoute? route) =>
        Routes.TryGetValue(serviceType, out route);
}
