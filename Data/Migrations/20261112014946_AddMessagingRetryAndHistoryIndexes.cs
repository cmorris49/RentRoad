using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentRoad.Data.Migrations
{
    /// <summary>Adds optional message submission identifiers and scoped retry and history indexes.</summary>
    public partial class AddMessagingRetryAndHistoryIndexes : Migration
    {
        /// <summary>Adds the nullable retry identifier and indexes while preserving existing messages and relationships.</summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientMessageId",
                table: "Messages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_MessageId",
                table: "Messages",
                columns: new[] { "ConversationId", "MessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_SenderId_ClientMessageId",
                table: "Messages",
                columns: new[] { "ConversationId", "SenderId", "ClientMessageId" },
                unique: true,
                filter: "[ClientMessageId] IS NOT NULL");
        }

        /// <summary>Removes only the message indexes and retry identifier introduced by this migration.</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_ConversationId_MessageId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ConversationId_SenderId_ClientMessageId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ClientMessageId",
                table: "Messages");
        }
    }
}
