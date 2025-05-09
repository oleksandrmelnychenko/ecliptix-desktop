using Avalonia.Controls;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public class SignUpHostViewModel : ReactiveObject
{
    public string Mobile { get; set; }
    
    public UserControl? ActiveView { get; set; }

    public SignUpHostViewModel()
    {
        
        
        
    }
    
}