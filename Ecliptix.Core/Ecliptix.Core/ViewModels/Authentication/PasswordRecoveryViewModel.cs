using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Authentication;

public class PasswordRecoveryViewModel : ReactiveObject
{
    private readonly ObservableAsPropertyHelper<string> _mobile;
    public string Mobile => _mobile.Value;
}