using System.Text.Json;

namespace ADProductScraper.Api;

public sealed class JobConflictException(string message) : Exception(message);
public sealed class JobStorageException(string message, Exception inner) : IOException(message, inner);

public static class ErrorHandling
{
    public static (int Status, string Message) Describe(Exception error) => error switch
    {
        BadHttpRequestException bad => (bad.StatusCode, "Invalid request. Check the submitted fields and JSON format."),
        ArgumentException => (400, error.Message),
        JobConflictException => (409, error.Message),
        JobStorageException => (503, error.Message),
        UnauthorizedAccessException => (503, "Storage access was denied. Check the application's data folder permissions."),
        IOException => (503, "Storage or connection failed. Check available disk space and retry."),
        HttpRequestException => (502, "The website could not be reached. Check your connection and retry."),
        TimeoutException or OperationCanceledException => (504, "The operation timed out. Please retry."),
        JsonException => (400, "Invalid JSON. Check the submitted fields."),
        _ => (500, "An unexpected error occurred. Retry the operation and use the error reference if it happens again.")
    };

    public static string CollectionMessage(Exception error) => error switch
    {
        HttpRequestException => error.Message,
        OperationCanceledException or TimeoutException => "The website or browser timed out. Use Resume to retry.",
        _ => Describe(error).Message
    };
}

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
            // Keep missing-job and access-denied responses readable by the UI.
            if (context.Request.Path.StartsWithSegments("/api") && !context.Response.HasStarted
                && context.Response.StatusCode >= 400 && context.Response.ContentLength is null
                && context.Response.ContentType is null)
            {
                var message = context.Response.StatusCode switch
                {
                    404 => "Collection or endpoint not found. Refresh the collection history.",
                    403 => "This request is not allowed from the current host or origin.",
                    405 => "This action does not support the request method.",
                    _ => "The request could not be completed. Check the submitted fields and retry."
                };
                await context.Response.WriteAsJsonAsync(new { error = message, traceId = context.TraceIdentifier }, context.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Client disconnected from {Path}", context.Request.Path);
            context.Abort();
        }
        catch (Exception error)
        {
            logger.LogError(error, "Request failed: {Method} {Path}; reference {TraceId}", context.Request.Method, context.Request.Path, context.TraceIdentifier);
            if (context.Response.HasStarted) { context.Abort(); return; }
            var (status, message) = ErrorHandling.Describe(error);
            context.Response.Clear();
            context.Response.StatusCode = status;
            try
            {
                await context.Response.WriteAsJsonAsync(new { error = message, traceId = context.TraceIdentifier }, context.RequestAborted);
            }
            catch (Exception writeError) when (writeError is IOException or OperationCanceledException)
            {
                logger.LogDebug(writeError, "Client could not receive error reference {TraceId}", context.TraceIdentifier);
                context.Abort();
            }
        }
    }
}
