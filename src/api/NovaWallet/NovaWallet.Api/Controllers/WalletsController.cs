using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Core.Models;
using NovaWallet.Core.Services;

namespace NovaWallet.Api.Controllers;

[Authorize]
[Route("wallets")]
public class WalletsController(WalletService walletService) : NovaWalletControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateWallet([FromBody] CreateWalletRequest request, CancellationToken cancellationToken)
    {
        var wallet = await walletService.CreateWalletAsync(request, cancellationToken);
        return Success(wallet, "Wallet created", StatusCodes.Status201Created);
    }

    [HttpGet]
    public async Task<IActionResult> GetBalance([FromQuery] Guid walletId, CancellationToken cancellationToken)
    {
        var wallet = await walletService.GetBalanceAsync(walletId, cancellationToken);
        return Success(wallet, "Balance retrieved");
    }

    [HttpPost("credit")]
    public async Task<IActionResult> Credit([FromQuery] Guid walletId, [FromBody] CreditWalletRequest request, CancellationToken cancellationToken)
    {
        var wallet = await walletService.CreditAsync(walletId, request, cancellationToken);
        return Success(wallet, "Wallet credited");
    }
}
