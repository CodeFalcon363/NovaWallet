namespace NovaWallet.Core.Interfaces;

/// <summary>
/// Resolves who is calling and from where, once per request, so audit writes never reach into
/// HttpContext directly and NovaWallet.Core stays free of an ASP.NET Core dependency.
/// </summary>
public interface ICallerContext
{
    string ActorId { get; }

    string IpAddress { get; }
}
