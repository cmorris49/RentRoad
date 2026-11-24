using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentRoad.Data.Migrations
{
    /// <summary>
    /// Extends trusted rental messages with per-action identity while preserving existing submission events.
    /// </summary>
    public partial class AddRentalApprovalMessageEvents : Migration
    {
        /// <summary>
        /// Adds a submission-compatible event type and permits one trusted message per rental action.
        /// </summary>
        /// <param name="migrationBuilder">The builder used to define the schema upgrade.</param>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_RentalRequestId",
                table: "Messages");

            migrationBuilder.AddColumn<int>(
                name: "RentalEventType",
                table: "Messages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_RentalRequestId_RentalEventType",
                table: "Messages",
                columns: new[] { "RentalRequestId", "RentalEventType" },
                unique: true,
                filter: "[RentalRequestId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Messages_RentalEventType",
                table: "Messages",
                sql: "[RentalEventType] IN (0, 1) AND ([RentalRequestId] IS NOT NULL OR [RentalEventType] = 0)");
        }

        /// <summary>
        /// Restores submission-only infrastructure when no approval event would lose its identity.
        /// </summary>
        /// <param name="migrationBuilder">The builder used to define the guarded schema downgrade.</param>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Messages] WITH (TABLOCKX, HOLDLOCK) WHERE [RentalEventType] <> 0)
                BEGIN
                    THROW 51013, 'Rollback blocked: rental approval events exist. Preserve historical messages and use a reviewed forward migration.', 1;
                END;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Messages_RentalRequestId_RentalEventType",
                table: "Messages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Messages_RentalEventType",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "RentalEventType",
                table: "Messages");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_RentalRequestId",
                table: "Messages",
                column: "RentalRequestId",
                unique: true,
                filter: "[RentalRequestId] IS NOT NULL");
        }
    }
}
