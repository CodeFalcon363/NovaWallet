using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace NovaWallet.Core.Data;

public static class SqlExceptionClassifier
{
    private const int SqlUniqueConstraintViolation = 2627;
    private const int SqlDuplicateKeyViolation = 2601;

    public static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx &&
        (sqlEx.Number == SqlUniqueConstraintViolation || sqlEx.Number == SqlDuplicateKeyViolation);
}
