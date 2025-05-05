using System.ComponentModel.DataAnnotations;
using Ecliptix.Core.Data;
using Ecliptix.Core.Factories;
using Ecliptix.Core.Services;

namespace Ecliptix.Core.ViewModels;

public class ForgotPasswordViewModel : PageViewModel
{
    public ForgotPasswordViewModel(INavigationWindowService navigationService, PageFactory pageFactory)
        : base(navigationService, pageFactory)
    {
        PageName = ApplicationPageNames.FORGOT_PASSWORD;
    }
    
    private string _phoneNumber = string.Empty;
    
    [Required(ErrorMessage = "Phone number is required")]
    public string PhoneNumber
    {
        get => _phoneNumber;
        set => SetProperty(ref _phoneNumber, value);
    }


    public bool CanSubmit =>
        !string.IsNullOrWhiteSpace(PhoneNumber);



}