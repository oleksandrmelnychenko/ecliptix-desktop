namespace Ecliptix.Core.Network;

public class PubKeyExchangeAction : RetryableAction
{
    private readonly PubKeyExchangeActionInvokable _pubKeyExchangeAction;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PubKeyExchangeAction" /> class.
    /// </summary>
    /// <param name="pubKeyExchangeAction">The public key exchange action.</param>
    public PubKeyExchangeAction(PubKeyExchangeActionInvokable pubKeyExchangeAction)
    {
        _pubKeyExchangeAction = pubKeyExchangeAction;
    }

    /// <summary>
    ///     Retrieves the request ID from the public key exchange action.
    /// </summary>
    /// <returns>The request ID.</returns>
    public override uint ReqId()
    {
        return _pubKeyExchangeAction.ReqId;
    }
}