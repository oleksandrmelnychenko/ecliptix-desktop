using Ecliptix.Protobuf.Transport.Common;

namespace Ecliptix.Network.Services.Network.Rpc;

internal static class GatewayRouteCatalog
{
    private static readonly Dictionary<RpcServiceType, GatewayRoute> Routes = new()
    {
        { RpcServiceType.GetServerPublicKeys, new GatewayRoute(EventContext.DeviceProvisioning, TransportEventType.DeviceServerKeys, DeliveryKind.Unary) },
        { RpcServiceType.RegisterAppDevice, new GatewayRoute(EventContext.DeviceProvisioning, TransportEventType.DeviceRegistration, DeliveryKind.Unary) },
        { RpcServiceType.EstablishSecrecyChannel, new GatewayRoute(EventContext.DeviceProvisioning, TransportEventType.DeviceSessionHandshake, DeliveryKind.Unary) },
        { RpcServiceType.RestoreSecrecyChannel, new GatewayRoute(EventContext.DeviceProvisioning, TransportEventType.DeviceSessionRecovery, DeliveryKind.Unary) },
        { RpcServiceType.EstablishAuthenticatedSecureChannel, new GatewayRoute(EventContext.DeviceProvisioning, TransportEventType.DeviceSessionAuthHandshake, DeliveryKind.Unary) },

        { RpcServiceType.ValidateMobileNumber, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityMobileNumberValidate, DeliveryKind.Unary) },
        { RpcServiceType.ValidateMobileForRecovery, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityRecoveryMobileVerify, DeliveryKind.Unary) },
        { RpcServiceType.CheckMobileNumberAvailability, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityMobileNumberAvailability, DeliveryKind.Unary) },
        { RpcServiceType.InitiateVerification, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityOtpInitiate, DeliveryKind.ServerStream) },
        { RpcServiceType.VerifyOtp, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityOtpVerify, DeliveryKind.Unary) },
        { RpcServiceType.GetMembershipState, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityMembershipStatus, DeliveryKind.Unary)},

        { RpcServiceType.RegistrationInit, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityOpaqueRegistrationInit, DeliveryKind.Unary) },
        { RpcServiceType.RegistrationComplete, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityOpaqueRegistrationComplete, DeliveryKind.Unary) },

        { RpcServiceType.RecoveryInit, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityOpaqueRecoveryInit, DeliveryKind.Unary) },
        { RpcServiceType.RecoveryComplete, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityOpaqueRecoveryComplete, DeliveryKind.Unary) },

        { RpcServiceType.SignInInitRequest, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityOpaqueSigninInit, DeliveryKind.Unary) },
        { RpcServiceType.SignInCompleteRequest, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityOpaqueSigninComplete, DeliveryKind.Unary) },

        { RpcServiceType.Logout, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentitySessionLogout, DeliveryKind.Unary) },
        { RpcServiceType.AnonymousLogout, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentitySessionLogoutAnonymous, DeliveryKind.Unary) },

        { RpcServiceType.ProfileLookup, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityProfileLookup, DeliveryKind.Unary) },
        { RpcServiceType.ProfileNameAvailability, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityProfileNameAvailability, DeliveryKind.Unary) },
        { RpcServiceType.ProfileUpsert, new GatewayRoute(EventContext.IdentityAccess, TransportEventType.IdentityProfileUpsert, DeliveryKind.Unary) }
    };

    public static bool TryGetRoute(RpcServiceType serviceType, out GatewayRoute? route) =>
        Routes.TryGetValue(serviceType, out route);
}
