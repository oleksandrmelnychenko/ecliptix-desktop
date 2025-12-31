namespace Ecliptix.Utilities;

public static class PhoneNumberHelper
{
    public static string CombineWithPrefix(string? prefix, string? numberBody)
    {
        string cleanPrefix = prefix?.Trim() ?? string.Empty;

        string processedBody = Normalize(numberBody);

        if (processedBody.StartsWith("+"))
        {
            return processedBody;
        }

        return $"{cleanPrefix}{processedBody}";
    }
    public static string Normalize(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return string.Empty;
        }
        return number
            .Replace(" ", "")
            .Replace("-", "")
            .Trim();
    }
}
