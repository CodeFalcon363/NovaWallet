using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaWallet.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogEntries",
                columns: table => new
                {
                    AuditId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    BalanceBeforeMinor = table.Column<long>(type: "bigint", nullable: true),
                    BalanceAfterMinor = table.Column<long>(type: "bigint", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntries", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "DailyOutboundUsages",
                columns: table => new
                {
                    WalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsageDateWat = table.Column<DateOnly>(type: "date", nullable: false),
                    OutboundTotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyOutboundUsages", x => new { x.WalletId, x.UsageDateWat });
                });

            migrationBuilder.CreateTable(
                name: "LedgerTransactions",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    BalanceAfterMinor = table.Column<long>(type: "bigint", nullable: false),
                    CounterpartyWalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerTransactions", x => x.TransactionId);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    OutboxMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.OutboxMessageId);
                });

            migrationBuilder.CreateTable(
                name: "TransferIdempotencyRecords",
                columns: table => new
                {
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResultTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferIdempotencyRecords", x => x.IdempotencyKey);
                });

            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    WalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    BalanceMinor = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.WalletId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_WalletId",
                table: "AuditLogEntries",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_WalletId_CreatedAtUtc",
                table: "LedgerTransactions",
                columns: new[] { "WalletId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAtUtc",
                table: "OutboxMessages",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_CustomerId",
                table: "Wallets",
                column: "CustomerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogEntries");

            migrationBuilder.DropTable(
                name: "DailyOutboundUsages");

            migrationBuilder.DropTable(
                name: "LedgerTransactions");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "TransferIdempotencyRecords");

            migrationBuilder.DropTable(
                name: "Wallets");
        }
    }
}
