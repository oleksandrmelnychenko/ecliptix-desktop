using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq; // Important for .Select()
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
        // Mock Data
        Contacts = new ObservableCollection<ContactItemViewModel>
        {
            new("Sarah Chen", "@sarahchen", true, ""), // Add real image paths if available
            new("Marcus Reid", "@marcusreid", false, ""),
            new("Emma Wilson", "@emmawilson", true, ""),
            new("Lisa Park", "@lisapark", true, ""),
            new("Alex Kumar", "@alexkumar", false, ""),
            new("John Doe", "@johndoe", false, ""),
            new("Jane Smith", "@janesmith", true, ""),
        };

        // Logic to handle selection (toggle behavior)
        SelectContactCommand = ReactiveCommand.Create<ContactItemViewModel>(contact =>
        {
            if (SelectedContact != null && SelectedContact != contact)
            {
                SelectedContact.IsSelected = false;
            }

            // If clicking the same one, maybe we want to keep it selected or toggle off?
            // Assuming strict selection for now:
            contact.IsSelected = true;
            SelectedContact = contact;
        });

        // FIX: Use .Select() to transform the value into a boolean
        // This resolves the "Ambiguous invocation" error.
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
