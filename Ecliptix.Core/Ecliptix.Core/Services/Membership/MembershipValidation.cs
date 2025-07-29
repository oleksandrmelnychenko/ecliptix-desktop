/*
using System;

namespace Ecliptix.Core.Services.Membership;

public static class MembershipValidation
{

    public static string Validate(ValidationType validationType, string value, ILocalizationService localizationService)
    {
        return validationType switch
        {
            ValidationType.MobileNumber => MobileNumberValidator.Validate(value, localizationService) ?? string.Empty,
            ValidationType.None => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(validationType), validationType, null)
        };
    }
}
*/
