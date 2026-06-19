using System.Net;
using System.Text.Json;
using PriceSaver.Server.Services;

namespace PriceSaver.Server.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, ITelegramAlertService alertService)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unhandled exception on {Method} {Path} | RequestId: {RequestId}",
                    context.Request.Method,
                    context.Request.Path,
                    context.TraceIdentifier);

                _ = alertService.SendErrorAlertAsync(
                    $"Unhandled exception on {context.Request.Method} {context.Request.Path}",
                    ex);

                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    context.Response.ContentType = "application/json";

                    var payload = JsonSerializer.Serialize(new
                    {
                        error = "An unexpected error occurred. Please try again later.",
                        requestId = context.TraceIdentifier
                    });

                    await context.Response.WriteAsync(payload);
                }
            }
        }
    }
}
