namespace Ecliptix.Utilities;

public static class PhoneNumberHelper
{
    public static string CombineWithPrefix(string? prefix, string? numberBody)
    {
        string cleanPrefix = prefix?.Trim() ?? string.Empty;
        string cleanBody = numberBody?.Trim() ?? string.Empty;

        if (cleanBody.StartsWith("+"))
        {
            return cleanBody;
        }

        return $"{cleanPrefix}{cleanBody}";
    }
}
