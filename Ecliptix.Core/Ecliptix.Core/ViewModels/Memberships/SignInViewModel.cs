using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public class SignInViewModel : ReactiveObject
{
    public string Mobile { get; set; }
    
    public string Password { get; set; }
    
    public SignInViewModel()
    {
    }
}