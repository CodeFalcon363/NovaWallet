using System.Security.Cryptography;
using System.Text;
using NovaWallet.Core.Models;

namespace NovaWallet.Core.Services;

public static class IdempotencyFingerprint
{
    public static string Compute(TransferRequest request)
    {
        var canonical = $"{request.SourceWalletId:N}:{request.DestinationWalletId:N}:{request.AmountMinor}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }
}
