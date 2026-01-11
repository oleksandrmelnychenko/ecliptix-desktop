
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Controls.TitleBarUtilities.ViewModels;

public class PersonalTagViewModel : ReactiveObject
{
    [Reactive] public string Name { get; set; }
    [Reactive] public string Tag { get; set; }

    public PersonalTagViewModel(string name, string tag)
    {
        Name = name;
        Tag = tag;
    }

    public PersonalTagViewModel()
    {
        Name = "Ecliptix";
        Tag = "@design.mode";
    }
}
