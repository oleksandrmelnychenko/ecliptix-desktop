namespace Ecliptix.Protocol.System.Utilities;

public readonly struct HashAlgorithmName
{
    public string Name { get; }

    private HashAlgorithmName(string name) => Name = name;

    public static HashAlgorithmName SHA256 => new("SHA256");
    public static HashAlgorithmName SHA384 => new("SHA384");
    public static HashAlgorithmName SHA512 => new("SHA512");
}