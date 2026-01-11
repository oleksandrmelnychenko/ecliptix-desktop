using System.Reactive;
using Ecliptix.Core.Shell.Messaging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Splat;
using IMessageBus = Ecliptix.Core.Messaging.Core.Messaging.IMessageBus;

namespace Ecliptix.Feature.NewContent.ViewModels;

public enum ChannelAccessType
{
    Public,
    Private
}

public class NewChannelViewModel : ReactiveObject
{
    private readonly IMessageBus? _messageBus;

    [Reactive] public ChannelAccessType SelectedAccessType { get; set; } = ChannelAccessType.Public;

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> ContinueCommand { get; }

    public NewChannelViewModel()
    {
        _messageBus = Locator.Current.GetService<IMessageBus>();

        CloseCommand = ReactiveCommand.Create(() =>
        {
            _messageBus?.PublishAsync(new CloseOverlayEvent());
        });

        ContinueCommand = ReactiveCommand.Create(() =>
        {
            System.Console.WriteLine($"Continue clicked. Type: {SelectedAccessType}");
        });
    }
}
