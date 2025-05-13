using System;
using Grpc.Core;
using ReactiveUI;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.ViewModels;

public class MainViewModel : ReactiveObject
{
    public MainViewModel()
    {
        SendRequestCommand = ReactiveCommand.Create(() =>
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

    public string StatusText { get; set; } = "Ready";
    public bool ShowContent { get; set; }
    public ReactiveCommand<Unit, Unit> SendRequestCommand { get; }
}