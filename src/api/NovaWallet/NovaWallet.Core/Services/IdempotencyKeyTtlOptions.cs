namespace NovaWallet.Core.Services;

public class IdempotencyKeyTtlOptions
{
    public const string SectionName = "IdempotencyKeyTtl";

    public int Hours { get; set; } = 24;
}
