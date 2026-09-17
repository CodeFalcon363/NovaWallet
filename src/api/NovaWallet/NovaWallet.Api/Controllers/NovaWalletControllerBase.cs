using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.Models;

namespace NovaWallet.Api.Controllers;

/// <summary>Centralizes the success envelope so individual actions never hand-build it (NFR-ARCH-9).</summary>
[ApiController]
public abstract class NovaWalletControllerBase : ControllerBase
{
    protected ObjectResult Success<T>(T data, string message, int statusCode = StatusCodes.Status200OK) =>
        StatusCode(statusCode, new ApiResponse<T>(statusCode, message, data));
}
