namespace KernelPrint.Engine.Exceptions;

/// <summary>
/// Thrown when the browser pool cannot accept a job immediately (backpressure).
/// </summary>
public sealed class ServiceSaturatedException : Exception
{
    public ServiceSaturatedException()
        : base("Print capacity is temporarily exceeded. Retry after a short delay.")
    {
    }
}
