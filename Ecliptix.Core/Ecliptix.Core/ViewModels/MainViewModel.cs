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

        public MainViewModel()
        {
            GrpcChannel grpcChannel = GrpcChannel.ForAddress("localhost:50051", new GrpcChannelOptions
            {
                UnsafeUseInsecureChannelCallCredentials = true,
            });
            Task t = grpcChannel.ConnectAsync();
            
            var interceptors = new Interceptor[]
            {
                new RequestMetaDataInterceptor()
            };

            grpcChannel.Intercept(interceptors);
            _client = new AppDeviceServiceActions.AppDeviceServiceActionsClient(grpcChannel);
            
            // Command to send gRPC request
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