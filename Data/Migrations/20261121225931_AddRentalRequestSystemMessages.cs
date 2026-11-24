using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentRoad.Data.Migrations
{
    /// <summary>Adds trusted rental request events and stable form submission identities without rewriting historical data.</summary>
    public partial class AddRentalRequestSystemMessages : Migration
    {
        /// <summary>Adds optional event and retry fields, preserving existing message authors and all historical records.</summary>
        /// <param name="migrationBuilder">The builder used to define the SQL Server schema upgrade.</param>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "RentalRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SenderId",
                table: "Messages",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<int>(
                name: "RentalRequestId",
                table: "Messages",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RentalRequests_RenterId_ClientRequestId",
                table: "RentalRequests",
                columns: new[] { "RenterId", "ClientRequestId" },
                unique: true,
                filter: "[ClientRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_RentalRequestId",
                table: "Messages",
                column: "RentalRequestId",
                unique: true,
                filter: "[RentalRequestId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Messages_Authorship",
                table: "Messages",
                sql: "([RentalRequestId] IS NULL AND [SenderId] IS NOT NULL) OR ([RentalRequestId] IS NOT NULL AND [SenderId] IS NULL AND [ClientMessageId] IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_RentalRequests_RentalRequestId",
                table: "Messages",
                column: "RentalRequestId",
                principalTable: "RentalRequests",
                principalColumn: "RentalRequestId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <summary>Reverts unused event infrastructure only when no authorless events or retry tokens would be lost.</summary>
        /// <param name="migrationBuilder">The builder used to define the guarded SQL Server schema downgrade.</param>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Messages] WITH (TABLOCKX, HOLDLOCK)
                           WHERE [SenderId] IS NULL OR [RentalRequestId] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [RentalRequests] WITH (TABLOCKX, HOLDLOCK)
                              WHERE [ClientRequestId] IS NOT NULL)
                BEGIN
                    THROW 51012, 'Rollback blocked: rental request system events or retry tokens exist. Preserve historical data and use a reviewed forward migration.', 1;
                END;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_RentalRequests_RentalRequestId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_RentalRequests_RenterId_ClientRequestId",
                table: "RentalRequests");

            migrationBuilder.DropIndex(
                name: "IX_Messages_RentalRequestId",
                table: "Messages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Messages_Authorship",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "RentalRequests");

            migrationBuilder.DropColumn(
                name: "RentalRequestId",
                table: "Messages");

            migrationBuilder.AlterColumn<string>(
                name: "SenderId",
                table: "Messages",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }
    }
}
