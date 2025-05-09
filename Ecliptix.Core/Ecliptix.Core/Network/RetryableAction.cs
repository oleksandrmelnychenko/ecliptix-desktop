namespace Ecliptix.Core.Network;

public abstract class RetryableAction
{
    /// <summary>
    ///     Retrieves the request ID associated with the action.
    /// </summary>
    /// <returns>The request ID.</returns>
    public abstract uint ReqId();
}