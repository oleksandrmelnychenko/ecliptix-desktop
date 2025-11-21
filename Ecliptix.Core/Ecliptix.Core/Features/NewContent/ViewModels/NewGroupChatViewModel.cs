using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Splat;
using IMessageBus = Ecliptix.Core.Core.Messaging.IMessageBus;

namespace Ecliptix.Core.Features.NewContent;

public class ContactItemViewModel : ReactiveObject
{
    public string Name { get; set; }
    public string Handle { get; set; }
    public string AvatarPath { get; set; }
    public bool IsOnline { get; set; }

    [Reactive] public bool IsSelected { get; set; }
}

public class NewGroupChatViewModel : ReactiveObject
{
    private readonly IMessageBus _messageBus;

    [Reactive] public string GroupName { get; set; }
    [Reactive] public string SearchQuery { get; set; }

    // Список всіх контактів
    public ObservableCollection<ContactItemViewModel> Contacts { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> CreateCommand { get; }
    public ReactiveCommand<Unit, Unit> UploadPhotoCommand { get; }

    public NewGroupChatViewModel()
    {
        _messageBus = Locator.Current.GetService<IMessageBus>();

        // Наповнюємо моковими даними (user1 ... user6)
        Contacts = new ObservableCollection<ContactItemViewModel>
        {
            new() { Name = "Sarah Chen", Handle = "@sarahchen", AvatarPath = "avares://Ecliptix.Core/Assets/DataSeed/user1.jpg", IsOnline = true },
            new() { Name = "Marcus Reid", Handle = "@marcusreid", AvatarPath = "avares://Ecliptix.Core/Assets/DataSeed/user2.jpg", IsOnline = false },
            new() { Name = "Emma Wilson", Handle = "@emmawilson", AvatarPath = "avares://Ecliptix.Core/Assets/DataSeed/user3.jpg", IsOnline = true },
            new() { Name = "Lisa Park", Handle = "@lisapark", AvatarPath = "avares://Ecliptix.Core/Assets/DataSeed/user4.jpg", IsOnline = true },
            new() { Name = "Alex Kumar", Handle = "@alexkumar", AvatarPath = "avares://Ecliptix.Core/Assets/DataSeed/user5.jpg", IsOnline = false },
            new() { Name = "John Doe", Handle = "@johndoe", AvatarPath = "avares://Ecliptix.Core/Assets/DataSeed/user6.jpg", IsOnline = false },
            // Дублюємо для тесту скролу
            new() { Name = "Sarah Chen", Handle = "@sarahchen", AvatarPath = "avares://Ecliptix.Core/Assets/DataSeed/user1.jpg", IsOnline = true },
            new() { Name = "Marcus Reid", Handle = "@marcusreid", AvatarPath = "avares://Ecliptix.Core/Assets/DataSeed/user2.jpg", IsOnline = false },
        };

        CloseCommand = ReactiveCommand.Create(() =>
        {
            _messageBus?.PublishAsync(new CloseOverlayEvent());
        });

        CreateCommand = ReactiveCommand.Create(() =>
        {
            List<ContactItemViewModel> selectedMembers = Contacts.Where(x => x.IsSelected).ToList();
            System.Console.WriteLine($"Creating group '{GroupName}' with {selectedMembers.Count} members");

            _messageBus?.PublishAsync(new CloseOverlayEvent());
        });

        UploadPhotoCommand = ReactiveCommand.Create(() =>
        {
            System.Console.WriteLine("Upload photo clicked");
        });
    }
}
