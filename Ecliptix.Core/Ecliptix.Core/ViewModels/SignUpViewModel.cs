using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using Ecliptix.Core.Data;
using Ecliptix.Core.Factories;
using Ecliptix.Core.Services;

namespace Ecliptix.Core.ViewModels;


public class SignUpViewModel : PageViewModel
{
    private string _email = string.Empty;
    private string _password = string.Empty;
    
    
    public SignUpViewModel(INavigationWindowService navigationService, PageFactory pageFactory) 
        : base(navigationService, pageFactory)
    {
        PageName = ApplicationPageNames.REGISTRATION;
    }

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    [Required(ErrorMessage = "Password is required")]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public bool CanSubmit =>
        !string.IsNullOrWhiteSpace(Email) &&
        !string.IsNullOrWhiteSpace(Password);
}