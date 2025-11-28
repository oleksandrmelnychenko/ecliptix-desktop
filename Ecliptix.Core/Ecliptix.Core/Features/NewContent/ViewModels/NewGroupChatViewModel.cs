using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Ecliptix.Core.Core.Messaging.Events;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Splat;
using IMessageBus = Ecliptix.Core.Core.Messaging.IMessageBus;

namespace Ecliptix.Core.Features.NewContent.ViewModels;

public class NewGroupChatViewModel : ReactiveObject
{
    private readonly IMessageBus _messageBus;

    [Reactive] public string GroupName { get; set; } = string.Empty;
    [Reactive] public string SearchQuery { get; set; } = string.Empty;

    // Список контактів (використовуємо той самий клас, що і в NewContact)
    public ObservableCollection<ContactItemViewModel> Contacts { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> CreateCommand { get; }
    public ReactiveCommand<Unit, Unit> UploadPhotoCommand { get; }

    public NewGroupChatViewModel()
    {
        _messageBus = Locator.Current.GetService<IMessageBus>();

        // Генеруємо демо-дані
        // Важливо: IsSelected тут працюватиме незалежно для кожного (як Checkbox)
        Contacts = new ObservableCollection<ContactItemViewModel>
        {
            new("Sarah Chen", "@sarahchen", true, ""),
            new("Marcus Reid", "@marcusreid", false, ""),
            new("Emma Wilson", "@emmawilson", true, ""),
            new("Lisa Park", "@lisapark", true, ""),
            new("Alex Kumar", "@alexkumar", false, ""),
            new("John Doe", "@johndoe", false, ""),
            new("Jane Smith", "@janesmith", true, "")
        };

        CloseCommand = ReactiveCommand.Create(() =>
        {
            // Закриваємо весь оверлей
            _messageBus?.PublishAsync(new CloseOverlayEvent());
        });

        // Команда створення доступна завжди, або можна додати умову (наприклад, вибрано > 0 людей)
        IObservable<bool> canCreate = this.WhenAnyValue(
            x => x.GroupName,
            name => !string.IsNullOrWhiteSpace(name));

        CreateCommand = ReactiveCommand.Create(() =>
        {
            List<ContactItemViewModel> selectedMembers = Contacts.Where(x => x.IsSelected).ToList();
            System.Diagnostics.Debug.WriteLine($"Creating group '{GroupName}' with {selectedMembers.Count} members");

            // Тут логіка створення групи через сервіс...

            _messageBus?.PublishAsync(new CloseOverlayEvent());
        }, canCreate);

        UploadPhotoCommand = ReactiveCommand.Create(() =>
        {
            System.Diagnostics.Debug.WriteLine("Open file dialog for photo...");
        });
    }
}
