using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public class ForgotPasswordViewModel : ReactiveObject
{
    private readonly ObservableAsPropertyHelper<string> _mobile;
    public string Mobile => _mobile.Value;
}