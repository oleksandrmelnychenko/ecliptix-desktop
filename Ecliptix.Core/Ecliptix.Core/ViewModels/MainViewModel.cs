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
   
    public string StatusText { get; set; } = "Ready";
    public bool ShowContent { get; set; }
    public ReactiveCommand<Unit, Unit> SendRequestCommand { get; }

    public MainViewModel()
    {

        SendRequestCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                StatusText = "Sending request...";

              
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