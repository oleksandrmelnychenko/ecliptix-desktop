namespace Ecliptix.Core.Network;

/// <summary>
///     Represents a service request action.
/// </summary>
public class ServiceAction : RetryableAction
{
    private readonly ServiceRequest _serviceRequest;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ServiceAction" /> class.
    /// </summary>
    /// <param name="serviceRequest">The service request.</param>
    public ServiceAction(ServiceRequest serviceRequest)
    {
        _serviceRequest = serviceRequest;
    }

    /// <summary>
    ///     Retrieves the request ID from the service request.
    /// </summary>
    /// <returns>The request ID.</returns>
    public override uint ReqId()
    {
        return _serviceRequest.ReqId;
    }
}