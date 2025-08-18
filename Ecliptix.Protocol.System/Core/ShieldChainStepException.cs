
namespace Ecliptix.Protocol.System.Core;

[Serializable]
public class ShieldChainStepException : Exception
{
    public ShieldChainStepException()
        : base("An error occurred within the ShieldChainStep operation.")
    {
    }

    public ShieldChainStepException(string message)
        : base(message)
    {
    }

    public ShieldChainStepException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}