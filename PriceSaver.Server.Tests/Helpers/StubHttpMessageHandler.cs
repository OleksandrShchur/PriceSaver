using System.Net;

namespace PriceSaver.Server.Tests.Helpers
{
    /// <summary>
    /// Minimal stub <see cref="HttpMessageHandler"/> that returns a canned response,
    /// allowing parsers that depend on <see cref="HttpClient"/> to be tested offline.
    /// </summary>
    public sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public static StubHttpMessageHandler WithBody(
            string body,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string mediaType = "application/json")
        {
            return new StubHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType)
            });
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responder(request));
        }
    }
}
