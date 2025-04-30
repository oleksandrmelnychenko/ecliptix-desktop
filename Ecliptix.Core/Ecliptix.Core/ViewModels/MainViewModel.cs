using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Interceptors;
using Ecliptix.Core.Network;
using Ecliptix.Core.Protocol;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.PubKeyExchange;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using ReactiveUI;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.ViewModels;

public class MainViewModel : ReactiveObject
{
    private readonly AppDeviceServiceActions.AppDeviceServiceActionsClient _client;
    private readonly ApplicationController _applicationController;

    private readonly EcliptixProtocolSystem _ecliptixProtocolSystem;

    public string StatusText { get; set; } = "Ready";
    public bool ShowContent { get; set; }
    public ReactiveCommand<Unit, Unit> SendRequestCommand { get; }

    public MainViewModel(AppDeviceServiceActions.AppDeviceServiceActionsClient client,
        ApplicationController applicationController)
    {
        EcliptixSystemIdentityKeys _aliceKeys = EcliptixSystemIdentityKeys.Create(5).Unwrap();
        ShieldSessionManager aliceSessionManager = ShieldSessionManager.Create();
        _ecliptixProtocolSystem = new EcliptixProtocolSystem(_aliceKeys, aliceSessionManager);

        _client = client;
        _applicationController = applicationController;

        SendRequestCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                StatusText = "Sending request...";
                (uint SessionId, PubKeyExchange InitialMessage) keyExchange =
                    await _ecliptixProtocolSystem.BeginDataCenterPubKeyExchangeAsync(PubKeyExchangeType
                        .AppDeviceEphemeralConnect);

                PubKeyExchange? response =
                    await _client.EstablishAppDeviceEphemeralConnectAsync(keyExchange.InitialMessage);

                (uint SessionId, SodiumSecureMemoryHandle RootKeyHandle) rootKeyHandle =
                    await _ecliptixProtocolSystem.CompleteDataCenterPubKeyExchangeAsync(keyExchange.SessionId,
                        PubKeyExchangeType.AppDeviceEphemeralConnect, response);

                byte[] appDevice = new AppDevice()
                {
                    DeviceId = ByteString.CopyFrom(applicationController.DeviceId.ToByteArray()),
                    DeviceType = AppDevice.Types.DeviceType.Desktop,
                    AppInstanceId = ByteString.CopyFrom(applicationController.AppInstanceId.ToByteArray()),
                }.ToByteArray();

                CipherPayload payload = await _ecliptixProtocolSystem.ProduceOutboundMessageAsync(
                    keyExchange.SessionId, PubKeyExchangeType.AppDeviceEphemeralConnect, appDevice
                );

                CipherPayload? regResp = await _client.RegisterDeviceAppIfNotExistAsync(payload);

                byte[] x = await _ecliptixProtocolSystem.ProcessInboundMessageAsync(keyExchange.SessionId,
                    PubKeyExchangeType.AppDeviceEphemeralConnect, regResp);

                AppDeviceRegisteredStateReply t = ServiceUtilities.ParseFromBytes<AppDeviceRegisteredStateReply>(x);

                ShowContent = !ShowContent;
            }
            catch (RpcException ex)
            {
                StatusText = $"Error: {ex.Status.Detail}";
                Console.WriteLine($"gRPC request failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                StatusText = $"Unexpected error: {ex.Message}";
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
        });
    }
}