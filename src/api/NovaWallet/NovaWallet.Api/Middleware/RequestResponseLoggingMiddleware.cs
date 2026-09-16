using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NovaWallet.Api.Middleware
{
    public class RequestResponseLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestResponseLoggingMiddleware> _logger;

        private static readonly string[] SensitiveKeys =
            ["password", "secret", "key", "token", "credential", "clientSecret", "connectionString"];

        public RequestResponseLoggingMiddleware(
            RequestDelegate next,
            ILogger<RequestResponseLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            context.Request.EnableBuffering();
            _logger.LogInformation($"Request: {context.Request.Method} {context.Request.Path}");

            if (context.Request.ContentType?.Contains("application/json") == true)
            {
                using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
                {
                    var requestBody = await reader.ReadToEndAsync();

                    Activity.Current?.AddEvent(new ActivityEvent("IncomingRequest",
                        tags: new ActivityTagsCollection
                        {
                            { "http.method", context.Request.Method },
                            { "http.path", context.Request.Path.ToString() },
                            { "http.request_body", RedactSensitiveFields(requestBody) }
                        }));
                }
                context.Request.Body.Position = 0;
            }

            var originalStream = context.Response.Body;

            // Security headers
            context.Response.Headers.TryAdd("X-XSS-Protection", "1; mode=block");
            context.Response.Headers.TryAdd("X-Frame-Options", "SAMEORIGIN");
            context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
            context.Response.Headers.TryAdd("Content-Security-Policy", "frame-ancestors 'self'");
            context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");

            using (var responseBody = new MemoryStream())
            {
                context.Response.Body = responseBody;
                try
                {
                    await _next(context);
                }
                finally
                {
                    // Restore the original stream before this middleware's buffer is disposed,
                    // so context.Response.Body never points at an already-released MemoryStream
                    // on an exception path (outer middleware may still try to write to it).
                    context.Response.Body = originalStream;
                }

                responseBody.Seek(0, SeekOrigin.Begin);
                await responseBody.CopyToAsync(originalStream);
                await originalStream.FlushAsync();
            }
        }

        private static string RedactSensitiveFields(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return body;
            var result = body;
            foreach (var key in SensitiveKeys)
                result = Regex.Replace(result,
                    $@"(""{key}""\s*:\s*)""[^""]*""",
                    $@"$1""[REDACTED]""",
                    RegexOptions.IgnoreCase);
            return result;
        }
    }
}
