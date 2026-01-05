using System.Collections.ObjectModel;
using ReactiveUI;

namespace Ecliptix.Feature.NewContent.NewContent.ViewModels;

public class CreateSelectionViewModel : ReactiveObject
{
    public ObservableCollection<CreateMenuItem> Items { get; }

    public ReactiveCommand<CreateActionType, CreateActionType> SelectActionCommand { get; }

    public CreateSelectionViewModel()
    {
        Items = new ObservableCollection<CreateMenuItem>();


        Items.Add(new CreateMenuItem(
            "New Post",
            "Share content with your followers",
            "M14,17H7V15H14M17,13H7V11H17M17,9H7V7H17M19,3H5C3.89,3 3,3.89 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5C21,3.89 20.1,3 19,3Z",
            CreateActionType.NewPost,
            "CONTENT",
            true));

        Items.Add(new CreateMenuItem(
            "New Contact",
            "Add someone to your contacts",
            "M15,14C12.33,14 7,15.33 7,18V20H23V18C23,15.33 17.67,14 15,14M6,10V7H4V10H1V12H4V15H6V12H9V10M15,12A4,4 0 0,0 19,8A4,4 0 0,0 15,4A4,4 0 0,0 11,8A4,4 0 0,0 15,12Z",
            CreateActionType.NewContact,
            "MESSAGING",
            true));

        Items.Add(new CreateMenuItem(
            "New Channel",
            "Create a broadcast channel",
            "M5.41,21L6.12,17H2.12L2.47,15H6.47L7.53,9H3.53L3.88,7H7.88L8.59,3H10.59L9.88,7H15.88L16.59,3H18.59L17.88,7H21.88L21.53,9H17.53L16.47,15H20.47L20.12,17H16.12L15.41,21H13.41L14.12,17H8.12L7.41,21H5.41M9.53,9L8.47,15H14.47L15.53,9H9.53Z",
            CreateActionType.NewChannel,
            string.Empty,
            false));

        Items.Add(new CreateMenuItem(
            "New Group",
            "Start a group conversation",
            "M12,6A3,3 0 0,0 9,9A3,3 0 0,0 12,12A3,3 0 0,0 15,9A3,3 0 0,0 12,6M6,8.17A2.5,2.5 0 0,0 3.5,10.67A2.5,2.5 0 0,0 6,13.17C6.88,13.17 7.65,12.71 8.09,12.03C7.42,11.18 7,10.15 7,9C7,8.8 7,8.6 7.04,8.4C6.72,8.25 6.37,8.17 6,8.17M12,14C10,14 6,15 6,17V19H18V17C18,15 14,14 12,14M4.67,14.97C3,15.26 1,16.04 1,17.33V19H5V17C5,16.22 5.29,15.53 5.74,14.95C5.35,14.96 4.98,14.97 4.67,14.97Z",
            CreateActionType.NewGroupChat,
            string.Empty,
            false));

        SelectActionCommand = ReactiveCommand.Create<CreateActionType, CreateActionType>(type => type);
    }
}

public record CreateMenuItem(string Title, string Description, string IconData, CreateActionType ActionType, string Category, bool IsFirstInCategory);
public enum CreateActionType
{
    NewChannel,
    NewGroupChat,
    NewPost,
    NewContact
}

