using System.Net;

namespace KernelPrint.Client;

public sealed class KernelPrintApiException : Exception
{
    public KernelPrintApiException(
        HttpStatusCode statusCode,
        string? correlationId,
        string? problemTitle,
        string? problemDetail,
        string? responseBody)
        : base(BuildMessage(statusCode, problemTitle, problemDetail))
    {
        StatusCode = statusCode;
        CorrelationId = correlationId;
        ProblemTitle = problemTitle;
        ProblemDetail = problemDetail;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string? CorrelationId { get; }

    public string? ProblemTitle { get; }

    public string? ProblemDetail { get; }

    public string? ResponseBody { get; }

    private static string BuildMessage(HttpStatusCode statusCode, string? title, string? detail)
    {
        if (!string.IsNullOrWhiteSpace(detail))
        {
            return $"KernelPrint API error {(int)statusCode}: {detail}";
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            return $"KernelPrint API error {(int)statusCode}: {title}";
        }

        return $"KernelPrint API error {(int)statusCode}";
    }
}
