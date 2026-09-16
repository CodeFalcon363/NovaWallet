using NovaWallet.Core.Data;
using NovaWallet.Core.Entities;
using NovaWallet.Core.Interfaces;

namespace NovaWallet.Core.Repositories;

public class AuditLogRepository(NovaWalletDbContext context) : IAuditLogRepository
{
    public void Append(AuditLogEntry entry) => context.AuditLogEntries.Add(entry);
}
