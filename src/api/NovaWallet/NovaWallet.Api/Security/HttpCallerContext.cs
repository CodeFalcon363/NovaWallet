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

    public string CorrelationId =>
        httpContextAccessor.HttpContext?.Items[CorrelationIdMiddleware.ItemsKey] as string ?? Guid.NewGuid().ToString();

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
