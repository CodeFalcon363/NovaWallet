using System.IdentityModel.Tokens.Jwt;
using NovaWallet.Api.Middleware;
using NovaWallet.Core.Interfaces;

namespace NovaWallet.Api.Security;

public class HttpCallerContext(IHttpContextAccessor httpContextAccessor) : ICallerContext
{
    private const string UnknownActor = "unknown";
    private const string UnknownIp = "unknown";

    public string ActorId =>
        httpContextAccessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? UnknownActor;

    public string CorrelationId
    {
        get
        {
            var context = httpContextAccessor.HttpContext;
            if (context is null)
            {
                // No HttpContext at all (e.g. a background job calling into a scoped service
                // outside a request) — a fresh value here is fine since there's no request to
                // correlate against in the first place.
                return Guid.NewGuid().ToString();
            }

            if (context.Items[CorrelationIdMiddleware.ItemsKey] is string existing)
            {
                return existing;
            }

            // CorrelationIdMiddleware always sets this in the real pipeline — this branch is a
            // safety net, not the expected path. Without caching the fallback back into Items,
            // each access here would mint a *different* GUID, so an audit entry and its sibling
            // outbox event for the same mutation could end up with different correlation IDs,
            // silently defeating the point of having one. Compute once, reuse for the rest of
            // the request.
            var fallback = Guid.NewGuid().ToString();
            context.Items[CorrelationIdMiddleware.ItemsKey] = fallback;
            return fallback;
        }
    }

    public string IpAddress
    {
        get
        {
            var context = httpContextAccessor.HttpContext;
            if (context is null)
            {
                return UnknownIp;
            }

            // Honor X-Forwarded-For when the service sits behind a reverse proxy/load balancer
            // (NFR-SEC-9); fall back to the direct connection's remote address otherwise.
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                return forwardedFor.Split(',')[0].Trim();
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? UnknownIp;
        }
    }
}
