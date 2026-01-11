using Ecliptix.Protobuf.Transport.Common;

namespace Ecliptix.Network.Services.Network.Rpc;

internal static class GatewayRouteCatalog
{
    private static readonly Dictionary<RpcServiceType, GatewayRoute> Routes = new()
    {
        { RpcServiceType.GetServerPublicKeys, new GatewayRoute(EventContext.DeviceProvisioning, TransportEventType.DeviceProvisioningGetServerPublicKeys, DeliveryKind.Unary) },
        { RpcServiceType.RegisterAppDevice, new GatewayRoute(EventContext.DeviceProvisioning, TransportEventType.DeviceProvisioningRegisterDevice, DeliveryKind.Unary) },
        { RpcServiceType.EstablishSecrecyChannel, new GatewayRoute(EventContext.DeviceProvisioning, TransportEventType.DeviceProvisioningSecureChannelEstablish, DeliveryKind.Unary) },
        { RpcServiceType.RestoreSecrecyChannel, new GatewayRoute(EventContext.DeviceProvisioning, TransportEventType.DeviceProvisioningSecureChannelRestore, DeliveryKind.Unary) },
        { RpcServiceType.EstablishAuthenticatedSecureChannel, new GatewayRoute(EventContext.DeviceProvisioning, TransportEventType.DeviceProvisioningSecureChannelAuthEstablish, DeliveryKind.Unary) },

        { RpcServiceType.ValidateMobileNumber, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessValidateMobileNumber, DeliveryKind.Unary) },
        { RpcServiceType.ValidateMobileForRecovery, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessRecoveryMobileVerification, DeliveryKind.Unary) },
        { RpcServiceType.CheckMobileNumberAvailability, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessCheckMobileAvailability, DeliveryKind.Unary) },
        { RpcServiceType.InitiateVerification, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessRecoveryMobileVerification, DeliveryKind.ServerStream) },
        { RpcServiceType.VerifyOtp, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessVerifyOtp, DeliveryKind.Unary) },

        { RpcServiceType.RegistrationInit, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessRegistrationInit, DeliveryKind.Unary) },
        { RpcServiceType.RegistrationComplete, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessRegistrationComplete, DeliveryKind.Unary) },

        { RpcServiceType.RecoverySecretKeyInit, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessRecoveryInit, DeliveryKind.Unary) },
        { RpcServiceType.RecoverySecretKeyComplete, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessRecoveryComplete, DeliveryKind.Unary) },

        { RpcServiceType.SignInInitRequest, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessSignInInit, DeliveryKind.Unary) },
        { RpcServiceType.SignInCompleteRequest, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessSignInComplete, DeliveryKind.Unary) },

        { RpcServiceType.Logout, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessLogout, DeliveryKind.Unary) },
        { RpcServiceType.AnonymousLogout, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessLogoutAnonymous, DeliveryKind.Unary) },

        { RpcServiceType.GetAccountProfile, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessGetProfile, DeliveryKind.Unary) },
        { RpcServiceType.CheckProfileNameAvailability, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessCheckProfileName, DeliveryKind.Unary) },
        { RpcServiceType.CreateOrUpdateProfile, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityAccessUpsertProfile, DeliveryKind.Unary) }
    };

    public static bool TryGetRoute(RpcServiceType serviceType, out GatewayRoute? route) =>
        Routes.TryGetValue(serviceType, out route);
}
