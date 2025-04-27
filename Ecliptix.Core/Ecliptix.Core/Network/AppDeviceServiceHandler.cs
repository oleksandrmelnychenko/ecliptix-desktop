using System;
using System.Threading.Tasks;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.PubKeyExchange;

namespace Ecliptix.Core.Network;

/// <summary>
/// Handles interactions with the AppDeviceServiceActions gRPC service.
/// Provides methods to establish an ephemeral connection and register a device or app if it does not exist.
/// </summary>
public class AppDeviceServiceHandler
{
    private readonly AppDeviceServiceActions.AppDeviceServiceActionsClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppDeviceServiceHandler"/> class.
    /// </summary>
    /// <param name="client">The gRPC client for AppDeviceServiceActions, injected via dependency injection.</param>
    public AppDeviceServiceHandler(AppDeviceServiceActions.AppDeviceServiceActionsClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Establishes an ephemeral connection with the service using the provided public key exchange request.
    /// </summary>
    /// <param name="request">The <see cref="PubKeyExchange"/> request containing the public key details.</param>
    /// <returns>A <see cref="Task{PubKeyExchange}"/> representing the asynchronous operation, yielding the service's response.</returns>
    /// <exception cref="Grpc.Core.RpcException">Thrown if the gRPC call fails.</exception>
    public async Task<PubKeyExchange> EstablishEphemeralConnectAsync(PubKeyExchange request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return await _client.EstablishAppDeviceEphemeralConnectAsync(request);
    }

    /// <summary>
    /// Registers a device or app with the service if it does not already exist, using the provided cipher payload.
    /// </summary>
    /// <param name="request">The <see cref="CipherPayload"/> request containing the registration details.</param>
    /// <returns>A <see cref="Task{CipherPayload}"/> representing the asynchronous operation, yielding the service's response.</returns>
    /// <exception cref="Grpc.Core.RpcException">Thrown if the gRPC call fails.</exception>
    public async Task<CipherPayload> RegisterDeviceAppIfNotExistAsync(CipherPayload request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return await _client.RegisterDeviceAppIfNotExistAsync(request);
    }
}