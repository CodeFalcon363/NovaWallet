namespace NovaWallet.Api.Models;

/// <summary>Uniform success-response envelope (NFR-ARCH-9). Errors stay RFC 7807 Problem Details.</summary>
public record ApiResponse<T>(int Status, string Message, T Data);
