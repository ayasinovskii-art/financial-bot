using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M001_InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "projection_offsets",
                schema: "app",
                columns: table => new
                {
                    projection_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    offset_value = table.Column<long>(type: "bigint", nullable: false),
                    last_updated = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projection_offsets", x => x.projection_name);
                });

            migrationBuilder.CreateTable(
                name: "system_heartbeat",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    last_seen = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_heartbeat", x => x.id);
                    table.CheckConstraint("ck_system_heartbeat_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_id = table.Column<long>(type: "bigint", nullable: false),
                    timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    settings_json = table.Column<string>(type: "jsonb", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    last_updated = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "whitelist",
                schema: "app",
                columns: table => new
                {
                    telegram_id = table.Column<long>(type: "bigint", nullable: false),
                    added_by = table.Column<long>(type: "bigint", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whitelist", x => x.telegram_id);
                });

            migrationBuilder.CreateTable(
                name: "periods",
                schema: "app",
                columns: table => new
                {
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    total_income = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    allocation_essentials = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    allocation_fun = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    allocation_deposit = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    savings_actual = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_periods", x => x.period_id);
                    table.ForeignKey(
                        name: "FK_periods_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expenses",
                schema: "app",
                columns: table => new
                {
                    expense_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bucket = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    needs_review = table.Column<bool>(type: "boolean", nullable: false),
                    auto_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    planned_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses", x => x.expense_id);
                    table.ForeignKey(
                        name: "FK_expenses_periods_period_id",
                        column: x => x.period_id,
                        principalSchema: "app",
                        principalTable: "periods",
                        principalColumn: "period_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expenses_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "incomes",
                schema: "app",
                columns: table => new
                {
                    income_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incomes", x => x.income_id);
                    table.ForeignKey(
                        name: "FK_incomes_periods_period_id",
                        column: x => x.period_id,
                        principalSchema: "app",
                        principalTable: "periods",
                        principalColumn: "period_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_incomes_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_period_id",
                schema: "app",
                table: "expenses",
                column: "period_id");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_user_id_occurred_at",
                schema: "app",
                table: "expenses",
                columns: new[] { "user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_user_id_period_id",
                schema: "app",
                table: "expenses",
                columns: new[] { "user_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_incomes_period_id",
                schema: "app",
                table: "incomes",
                column: "period_id");

            migrationBuilder.CreateIndex(
                name: "IX_incomes_user_id_period_id",
                schema: "app",
                table: "incomes",
                columns: new[] { "user_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_periods_user_id_status",
                schema: "app",
                table: "periods",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_users_telegram_id",
                schema: "app",
                table: "users",
                column: "telegram_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expenses",
                schema: "app");

            migrationBuilder.DropTable(
                name: "incomes",
                schema: "app");

            migrationBuilder.DropTable(
                name: "projection_offsets",
                schema: "app");

            migrationBuilder.DropTable(
                name: "system_heartbeat",
                schema: "app");

            migrationBuilder.DropTable(
                name: "whitelist",
                schema: "app");

            migrationBuilder.DropTable(
                name: "periods",
                schema: "app");

            migrationBuilder.DropTable(
                name: "users",
                schema: "app");
        }
    }
}
