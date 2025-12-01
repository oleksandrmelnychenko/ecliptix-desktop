using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.NewContent.ViewModels;

public class NewContactViewModel : ReactiveObject
{
    [Reactive] public string SearchText { get; set; } = string.Empty;
    [Reactive] public ContactItemViewModel? SelectedContact { get; set; }

    public ObservableCollection<ContactItemViewModel> Contacts { get; }

    public ReactiveCommand<Unit, Unit> StartChatCommand { get; }
    public ReactiveCommand<ContactItemViewModel, Unit> SelectContactCommand { get; }

    public NewContactViewModel()
    {
        Contacts = new ObservableCollection<ContactItemViewModel>
        {
            new("Sarah Chen", "@sarahchen", true, "$avares://Ecliptix.Core/Assets/DataSeed/user1.jpg"),
            new("Marcus Reid", "@marcusreid", false, "$avares://Ecliptix.Core/Assets/DataSeed/user6.jpg"),
            new("Emma Wilson", "@emmawilson", true, "$avares://Ecliptix.Core/Assets/DataSeed/user5.jpg"),
            new("Lisa Park", "@lisapark", true, "$avares://Ecliptix.Core/Assets/DataSeed/user4.jpg"),
            new("Alex Kumar", "@alexkumar", false, "$avares://Ecliptix.Core/Assets/DataSeed/user3.jpg"),
            new("John Doe", "@johndoe", false, "$avares://Ecliptix.Core/Assets/DataSeed/user2.jpg"),
            new("Jane Smith", "@janesmith", true, "$avares://Ecliptix.Core/Assets/DataSeed/user1.jpg"),
        };

        SelectContactCommand = ReactiveCommand.Create<ContactItemViewModel>(contact =>
        {
            if (SelectedContact != null && SelectedContact != contact)
            {
                SelectedContact.IsSelected = false;
            }

            contact.IsSelected = true;
            SelectedContact = contact;
        });

        IObservable<bool> canStartChat = this.WhenAnyValue(x => x.SelectedContact)
                               .Select(contact => contact != null);

        StartChatCommand = ReactiveCommand.Create(() =>
        {
            System.Diagnostics.Debug.WriteLine($"Starting chat with {SelectedContact?.DisplayName}");
        }, canStartChat);
    }
}

public class ContactItemViewModel : ReactiveObject
{
    public string DisplayName { get; }
    public string Handle { get; }
    public bool IsOnline { get; }
    public string AvatarUrl { get; }

    [Reactive] public bool IsSelected { get; set; }

    public ContactItemViewModel(string displayName, string handle, bool isOnline, string avatarUrl)
    {
        DisplayName = displayName;
        Handle = handle;
        IsOnline = isOnline;
        AvatarUrl = avatarUrl;
    }
}
