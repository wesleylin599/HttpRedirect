using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace HttpRedirect
{
    public static class RedirectExtensions
    {
        public static IServiceCollection AddRedirect(
            this IServiceCollection services,
            string redirectUrl
        ) => services.AddRedirect(redirectUrl, context => !context.WebSockets.IsWebSocketRequest);

        public static IServiceCollection AddRedirectWithHeader(
            this IServiceCollection services,
            string httpHeaderKey
        ) =>
            services.AddRedirect(
                context =>
                {
                    StringValues strings;
                    if (context.Request.Headers.TryGetValue(httpHeaderKey, out strings))
                    {
                        var redirectUrl = strings.Last();
                        if (string.IsNullOrEmpty(redirectUrl))
                        {
                            throw new Exception($"{httpHeaderKey} header is empty");
                        }
                        var host = redirectUrl.Split(':')[0];
                        var port = (string)(redirectUrl.Split(':').GetValue(1) ?? "-1");
                        var uriBuilder = new UriBuilder();
                        uriBuilder.Scheme = context.Request.Scheme;
                        uriBuilder.Host = host;
                        uriBuilder.Port = int.Parse(port);
                        uriBuilder.Path = context.Request.Path;
                        uriBuilder.Query = context.Request.QueryString.Value;
                        return uriBuilder.Uri;
                    }
                    else
                    {
                        throw new Exception($"{httpHeaderKey} header is missing");
                    }
                },
                context =>
                    context.Request.Headers.ContainsKey(httpHeaderKey)
                    && !context.WebSockets.IsWebSocketRequest
            );

        public static IServiceCollection AddRedirect(
            this IServiceCollection services,
            string redirectUrl,
            Func<HttpContext, bool> filter
        ) =>
            services.AddRedirect(
                context =>
                {
                    var host = redirectUrl.Split(':')[0];
                    var port = (string)(redirectUrl.Split(':').GetValue(1) ?? "-1");
                    var uriBuilder = new UriBuilder();
                    uriBuilder.Scheme = context.Request.Scheme;
                    uriBuilder.Host = host;
                    uriBuilder.Port = int.Parse(port);
                    uriBuilder.Path = context.Request.Path;
                    uriBuilder.Query = context.Request.QueryString.Value;
                    return uriBuilder.Uri;
                },
                filter
            );

        public static IServiceCollection AddRedirect(
            this IServiceCollection services,
            Func<HttpContext, Uri> redirectUrl,
            Func<HttpContext, bool> filter
        )
        {
            services
                .AddOptions<RedirectOptions>()
                .Configure(options =>
                {
                    options.Filter = filter;
                    options.RedirectUrl = redirectUrl;
                });
            services
                .AddHttpClient("redirect_httpClient")
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = delegate
                        {
                            return true;
                        },
                    });
            services.AddTransient<RedirectMiddleware>();
            return services;
        }

        public static IApplicationBuilder UseRedirect(this IApplicationBuilder app)
        {
            app.UseMiddleware<RedirectMiddleware>();
            return app;
        }
    }
}
