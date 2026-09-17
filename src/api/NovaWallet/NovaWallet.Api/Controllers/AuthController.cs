using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.Security;

namespace NovaWallet.Api.Controllers;

/// <summary>
/// Dev/mock token issuance only — see DevTokenIssuer. Not a real identity provider; the task
/// brief explicitly scopes JWT issuance out ("a simplified/mock issuer is fine").
/// </summary>
[Route("auth")]
public class AuthController(DevTokenIssuer tokenIssuer) : NovaWalletControllerBase
{
    [HttpPost("tokens")]
    public IActionResult IssueToken([FromBody] IssueTokenRequest request)
    {
        var token = tokenIssuer.IssueToken(request.CustomerId);
        return Success(new TokenResponse(token), "Token issued");
    }
}
