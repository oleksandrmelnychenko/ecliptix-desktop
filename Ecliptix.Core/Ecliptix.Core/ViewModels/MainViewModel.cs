using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Interceptors;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.PubKeyExchange;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels
{
    public class MainViewModel : ReactiveObject, IDisposable
    {
        private readonly Grpc.Net.Client.GrpcChannel _channel;
        private readonly AppDeviceServiceActions.AppDeviceServiceActionsClient _client;

        public string StatusText { get; set; } = "Ready";
        public bool ShowContent { get; set; }
        public ReactiveCommand<Unit, Unit> SendRequestCommand { get; }

        public MainViewModel(AppDeviceServiceActions.AppDeviceServiceActionsClient client)
        {
            _client = client;
            
            SendRequestCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                try
                {
                    StatusText = "Sending request...";
                    var request = new PubKeyExchange(); // Populate fields as needed
                    var response = await _client.EstablishAppDeviceEphemeralConnectAsync(request);
                    StatusText = $"Success: {response}";
                    ShowContent = !ShowContent; // Toggle content visibility
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

        // Dispose resources
        public void Dispose()
        {
            try
            {
                _channel?.ShutdownAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to shut down gRPC channel: {ex.Message}");
            }
        }
    }
}