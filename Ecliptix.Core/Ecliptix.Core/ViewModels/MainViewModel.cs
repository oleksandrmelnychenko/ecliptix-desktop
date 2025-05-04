using System;
using Ecliptix.Core.Network;
using Ecliptix.Core.Protocol;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.PubKeyExchange;
using Google.Protobuf;
using Grpc.Core;
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
        EcliptixSystemIdentityKeys aliceKeys = EcliptixSystemIdentityKeys.Create(5).Unwrap();
        _ecliptixProtocolSystem = new EcliptixProtocolSystem(aliceKeys);

        _client = client;
        _applicationController = applicationController;

        SendRequestCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                StatusText = "Sending request...";

                PubKeyExchange keyExchange =
                    _ecliptixProtocolSystem.BeginDataCenterPubKeyExchange(1, PubKeyExchangeType
                        .AppDeviceEphemeralConnect);

                PubKeyExchange? response =
                    await _client.EstablishAppDeviceEphemeralConnectAsync(keyExchange);

                _ecliptixProtocolSystem.CompleteDataCenterPubKeyExchange(1,
                    PubKeyExchangeType.AppDeviceEphemeralConnect, response);

                byte[] appDevice = new AppDevice()
                {
                    DeviceId = ByteString.CopyFrom(applicationController.DeviceId.ToByteArray()),
                    DeviceType = AppDevice.Types.DeviceType.Desktop,
                    AppInstanceId = ByteString.CopyFrom(applicationController.AppInstanceId.ToByteArray()),
                }.ToByteArray();

                CipherPayload payload = _ecliptixProtocolSystem.ProduceOutboundMessage(
                    1, PubKeyExchangeType.AppDeviceEphemeralConnect, appDevice
                );

                CipherPayload? regResp = await _client.RegisterDeviceAppIfNotExistAsync(payload);

                byte[] x = _ecliptixProtocolSystem.ProcessInboundMessage(1,
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