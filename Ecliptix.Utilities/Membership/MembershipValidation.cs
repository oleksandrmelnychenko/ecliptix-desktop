namespace Ecliptix.Utilities.Membership;

public static class MembershipValidation
{

    public static string Validate(ValidationType validationType, string value)
    {
        return validationType switch
        {
            ValidationType.PhoneNumber => PhoneNumberValidator.Validate(value) ?? string.Empty,
            ValidationType.SecureKey => SecureKeyValidator.Validate(value) ?? string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(validationType), validationType, null)
        };
    }
   
    
}