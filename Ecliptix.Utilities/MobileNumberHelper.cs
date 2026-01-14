namespace Ecliptix.Utilities;

public static class MobileNumberHelper
{
    public static string CombineWithPrefix(string? prefix, string? numberBody)
    {
        string processedBody = Normalize(numberBody);

        if (processedBody.StartsWith('+'))
        {
            return processedBody;
        }

        ReadOnlySpan<char> cleanPrefix = prefix.AsSpan().Trim();
        return cleanPrefix.IsEmpty ? processedBody : string.Concat(cleanPrefix, processedBody);
    }

    public static string Normalize(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return string.Empty;
        }

        ReadOnlySpan<char> span = number.AsSpan().Trim();

        int count = 0;
        foreach (char c in span)
        {
            if (c is not ' ' and not '-')
            {
                count++;
            }
        }

        if (count == 0)
        {
            return string.Empty;
        }

        if (count == span.Length)
        {
            return span.ToString();
        }

        return string.Create(count, span, static (dest, src) =>
        {
            int idx = 0;
            foreach (char c in src)
            {
                if (c is not ' ' and not '-')
                {
                    dest[idx++] = c;
                }
            }
        });
    }
}
