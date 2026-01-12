using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.SecureKeyConfirmation;

public class RequirementItem(string text, bool isMet) : ReactiveObject
{
    public string Text { get; } = text;

    [Reactive] public bool IsMet { get; set; } = isMet;
}
