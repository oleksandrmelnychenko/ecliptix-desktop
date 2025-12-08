using System.Reactive;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Splat;
using IMessageBus = Ecliptix.Core.Core.Messaging.IMessageBus;

namespace Ecliptix.Core.Views.Memberships.Components.TitleBarUtilities.ViewModels;

public record ToggleThemeEvent(bool IsDarkMode);

public class ToggleThemeViewModel: ReactiveObject
{
    private readonly IMessageBus? _messageBus;

    [Reactive] public bool IsDarkMode { get; set; } = false;

    public ReactiveCommand<Unit, Unit> ToggleCommand { get; }

    public ToggleThemeViewModel()
    {
        _messageBus = Locator.Current.GetService<IMessageBus>();

        ToggleCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsDarkMode = !IsDarkMode;

            if (_messageBus != null)
            {
                await _messageBus.PublishAsync(new ToggleThemeEvent(IsDarkMode));
            }
        });
    }

}
