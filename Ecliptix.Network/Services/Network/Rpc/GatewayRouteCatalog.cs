using Ecliptix.Protobuf.Transport.Common;
using Ecliptix.Protobuf.Transport.DeviceProvisioning;
using Ecliptix.Protobuf.Transport.Identity;

namespace Ecliptix.Network.Services.Network.Rpc;

internal static class GatewayRouteCatalog
{
    private static readonly Dictionary<RpcServiceType, GatewayRoute> Routes = new()
    {
        { RpcServiceType.RegisterAppDevice, new GatewayRoute("device_provisioning", DeviceProvisioningEventType.DeviceProvisioningRegisterDevice.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.EstablishSecrecyChannel, new GatewayRoute("device_provisioning", DeviceProvisioningEventType.DeviceProvisioningSecureChannelEstablish.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.RestoreSecrecyChannel, new GatewayRoute("device_provisioning", DeviceProvisioningEventType.DeviceProvisioningSecureChannelRestore.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.EstablishAuthenticatedSecureChannel, new GatewayRoute("device_provisioning", DeviceProvisioningEventType.DeviceProvisioningSecureChannelAuthEstablish.ToString(), DeliveryKind.Unary) },

        { RpcServiceType.ValidateMobileNumber, new GatewayRoute("identity_access", IdentityAccessEventType.IdentityAccessValidateMobileNumber.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.CheckMobileNumberAvailability, new GatewayRoute("identity_access", IdentityAccessEventType.IdentityAccessCheckMobileAvailability.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.InitiateVerification, new GatewayRoute("identity_access", IdentityAccessEventType.IdentityAccessRecoveryMobileVerification.ToString(), DeliveryKind.ServerStream) },
        { RpcServiceType.VerifyOtp, new GatewayRoute("identity_access", IdentityAccessEventType.IdentityAccessVerifyOtp.ToString(), DeliveryKind.Unary) },

        { RpcServiceType.RegistrationInit, new GatewayRoute("identity_access", IdentityAccessEventType.IdentityAccessRegistrationInit.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.RegistrationComplete, new GatewayRoute("identity_access", IdentityAccessEventType.IdentityAccessRegistrationComplete.ToString(), DeliveryKind.Unary) },

        { RpcServiceType.RecoverySecretKeyInit, new GatewayRoute("identity_access", IdentityAccessEventType.IdentityAccessRecoveryInit.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.RecoverySecretKeyComplete, new GatewayRoute("identity_access", IdentityAccessEventType.IdentityAccessRecoveryComplete.ToString(), DeliveryKind.Unary) },

        { RpcServiceType.SignInInitRequest, new GatewayRoute("identity_access", IdentityAccessEventType.IdentityAccessSignInInit.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.SignInCompleteRequest, new GatewayRoute("identity_access", IdentityAccessEventType.IdentityAccessSignInComplete.ToString(), DeliveryKind.Unary) },

        { RpcServiceType.Logout, new GatewayRoute("identity_access", IdentityAccessEventType.IdentityAccessLogout.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.AnonymousLogout, new GatewayRoute("identity_access", IdentityAccessEventType.IdentityAccessLogoutAnonymous.ToString(), DeliveryKind.Unary) },

        { RpcServiceType.GetAccountProfile, new GatewayRoute("identity_access", IdentityAccessEventType.IdentityAccessGetProfile.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.CheckProfileNameAvailability, new GatewayRoute("identity_access", IdentityAccessEventType.IdentityAccessCheckProfileName.ToString(), DeliveryKind.Unary) },
        { RpcServiceType.CreateOrUpdateProfile, new GatewayRoute("identity_access", IdentityAccessEventType.IdentityAccessUpsertProfile.ToString(), DeliveryKind.Unary) }
    };

    public static bool TryGetRoute(RpcServiceType serviceType, out GatewayRoute? route) =>
        Routes.TryGetValue(serviceType, out route);
}
