using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NovaWallet.Api.Security;
using NovaWallet.Core.Models;
using NovaWallet.Core.Services;

namespace NovaWallet.Api.Controllers;

[Authorize]
[Route("transfers")]
public class TransfersController(TransferService transferService) : NovaWalletControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimiterPolicies.Transfer)]
    public async Task<IActionResult> Transfer(
        [FromHeader(Name = "Idempotency-Key")][Required][StringLength(128, MinimumLength = 1)] string idempotencyKey,
        [FromBody] TransferRequest request,
        CancellationToken cancellationToken)
    {
        var result = await transferService.TransferAsync(idempotencyKey, request, cancellationToken);
        return Success(result, "Transfer completed");
    }
}
