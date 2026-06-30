using System.Net.Http;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HttpRedirect
{
    public class RedirectMiddleware : IMiddleware
    {
        private readonly RedirectOptions _options;
        private readonly ILogger<RedirectMiddleware> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public RedirectMiddleware(
            IOptions<RedirectOptions> options,
            ILogger<RedirectMiddleware> logger,
            IHttpClientFactory httpClientFactory
        )
        {
            _options = options.Value;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            if (!_options.Filter.Invoke(context))
            {
                await next(context);
                return;
            }

            var uri = _options.RedirectUrl.Invoke(context);
            _logger.LogInformation("Redirecting request to {Url}", uri.ToString());
            CancellationToken cancellationToken = context.RequestAborted;

            try
            {
                using var client = _httpClientFactory.CreateClient("redirect_httpClient");
                using var request = new HttpRequestMessage(
                    new HttpMethod(context.Request.Method),
                    uri
                )
                {
                    Version = context.Request.Protocol switch
                    {
                        "HTTP/2" => new Version(2, 0),
                        "HTTP/3" => new Version(3, 0),
                        _ => new Version(1, 1),
                    },
                };

                if (
                    context.Request.ContentLength > 0
                    || context.Request.Headers.ContainsKey("Transfer-Encoding")
                )
                {
                    request.Content = new StreamContent(context.Request.Body);
                }

                foreach (var header in context.Request.Headers)
                {
                    if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (
                        !request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray())
                        && request.Content is not null
                    )
                    {
                        request.Content.Headers.TryAddWithoutValidation(
                            header.Key,
                            header.Value.ToArray()
                        );
                    }
                }

                using var responseMessage = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                );

                context.Response.StatusCode = (int)responseMessage.StatusCode;
                foreach (var header in responseMessage.Headers)
                {
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }

                foreach (var header in responseMessage.Content.Headers)
                {
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }

                context.Response.Headers.Remove("transfer-encoding");
                await responseMessage.Content.CopyToAsync(context.Response.Body, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to redirect request to {Url}", uri);
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync(ex.ToString(), cancellationToken);
            }
        }
    }
}
