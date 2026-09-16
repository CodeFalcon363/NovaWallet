using System.ComponentModel.DataAnnotations;

namespace NovaWallet.Core.Entities;

/// <summary>
/// Incrementally-maintained counter of a wallet's outbound transfer total for one WAT calendar day.
/// Updated atomically within the transfer transaction to avoid a SUM() aggregate per transfer.
/// </summary>
public class DailyOutboundUsage
{
    public Guid WalletId { get; set; }

    public DateOnly UsageDateWat { get; set; }

    [Range(0, long.MaxValue)]
    public long OutboundTotalMinor { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
