using System.Reactive;
using System.Reactive.Linq;
using Ecliptix.Core.Shell.Messaging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Splat;
using IMessageBus = Ecliptix.Core.Messaging.Core.Messaging.IMessageBus;

namespace Ecliptix.Feature.NewContent.ViewModels;

public class NewPostViewModel : ReactiveObject
{
    private readonly IMessageBus? _messageBus;

    [Reactive] public string Caption { get; set; } = "";
    [Reactive] public string Location { get; set; } = string.Empty;
    [Reactive] public bool HideLikeViewCounts { get; set; }
    [Reactive] public bool TurnOffCommenting { get; set; }

    [ObservableAsProperty] public int CaptionLength { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> ShareCommand { get; }
    public ReactiveCommand<Unit, Unit> AddMediaCommand { get; }

    public NewPostViewModel()
    {
        _messageBus = Locator.Current.GetService<IMessageBus>();

        this.WhenAnyValue(x => x.Caption)
            .Select(text => text?.Length ?? 0)
            .ToPropertyEx(this, x => x.CaptionLength);

        CloseCommand = ReactiveCommand.Create(() =>
        {
            _messageBus?.PublishAsync(new CloseOverlayEvent());
        });

        ShareCommand = ReactiveCommand.Create(() =>
        {
            System.Console.WriteLine($"Sharing post: {Caption} (Loc: {Location})");
            _messageBus?.PublishAsync(new CloseOverlayEvent());
        });

        AddMediaCommand = ReactiveCommand.Create(() =>
        {
            System.Console.WriteLine("Add Media Clicked");
        });
    }
}
