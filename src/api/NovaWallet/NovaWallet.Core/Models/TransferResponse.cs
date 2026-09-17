namespace NovaWallet.Core.Models;

public record TransferResponse(Guid TransferId, long NewSourceBalanceMinor);
