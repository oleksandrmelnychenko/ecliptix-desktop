using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Splat;
using IMessageBus = Ecliptix.Core.Core.Messaging.IMessageBus;

namespace Ecliptix.Core.Features.NewContent;

public class NewPostViewModel : ReactiveObject
{
    private readonly IMessageBus _messageBus;

    [Reactive] public string Caption { get; set; } = "";
    [Reactive] public string Location { get; set; }
    [Reactive] public bool HideLikeViewCounts { get; set; }
    [Reactive] public bool TurnOffCommenting { get; set; }

    // Властивість для лічильника символів, яка автоматично оновлюється
    [ObservableAsProperty] public int CaptionLength { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> ShareCommand { get; }
    public ReactiveCommand<Unit, Unit> AddMediaCommand { get; }

    public NewPostViewModel()
    {
        _messageBus = Locator.Current.GetService<IMessageBus>();


        // Правильний спосіб для ObservableAsProperty:
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
