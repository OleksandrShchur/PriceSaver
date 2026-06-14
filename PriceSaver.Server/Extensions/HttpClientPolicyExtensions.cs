using Polly;
using Polly.Retry;
using System.Net;
using System.Net.Http;

namespace PriceSaver.Server.Extensions
{
    internal static class HttpClientPolicyExtensions
    {
        public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests
                                 || msg.StatusCode == HttpStatusCode.RequestTimeout
                                 || msg.StatusCode == HttpStatusCode.InternalServerError
                                 || msg.StatusCode == HttpStatusCode.BadGateway
                                 || msg.StatusCode == HttpStatusCode.ServiceUnavailable
                                 || msg.StatusCode == HttpStatusCode.GatewayTimeout)
                .WaitAndRetryAsync(
                    retryCount: 5,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt - 1), 10)),
                    onRetry: (outcome, timespan, retryAttempt, context) =>
                    {
                        // Optional: add telemetry or logging here in the future.
                    });
        }
    }
}
