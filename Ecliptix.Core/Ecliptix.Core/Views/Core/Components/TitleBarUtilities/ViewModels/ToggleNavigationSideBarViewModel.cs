using System.Reactive;
using ReactiveUI;
using Splat;
using IMessageBus = Ecliptix.Core.Messaging.Core.Messaging.IMessageBus;

namespace Ecliptix.Core.Views.Core.Components.TitleBarUtilities.ViewModels;

public record ToggleSidebarEvent;

public class ToggleNavigationSideBarViewModel : ReactiveObject
{
    public ReactiveCommand<Unit, Unit> ToggleCommand { get; }

    public ToggleNavigationSideBarViewModel()
    {
        IMessageBus? messageBus = Locator.Current.GetService<IMessageBus>();

        ToggleCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (messageBus != null)
            {
                await messageBus.PublishAsync(new ToggleSidebarEvent());
            }
        });
    }
}
