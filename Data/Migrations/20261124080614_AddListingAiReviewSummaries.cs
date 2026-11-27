using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentRoad.Data.Migrations
{
    /// <summary>
    /// Adds an initially empty derived summary cache without changing existing rental or review records.
    /// </summary>
    public partial class AddListingAiReviewSummaries : Migration
    {
        /// <summary>
        /// Creates the per-listing cache, bounded generation claims and restrictive listing relationship.
        /// </summary>
        /// <param name="migrationBuilder">The migration operation builder.</param>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ListingReviewSummaries",
                columns: table => new
                {
                    ListingId = table.Column<int>(type: "int", nullable: false),
                    SummaryText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ContentFingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SummarizedReviewCount = table.Column<int>(type: "int", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GenerationExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AttemptWindowStartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AttemptsInWindow = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingReviewSummaries", x => x.ListingId);
                    table.CheckConstraint("CK_ListingReviewSummaries_Claim", "([GenerationId] IS NULL AND [GenerationExpiresAtUtc] IS NULL) OR ([GenerationId] IS NOT NULL AND [GenerationExpiresAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_ListingReviewSummaries_Content", "([SummaryText] IS NULL AND [ContentFingerprint] IS NULL AND [Model] IS NULL AND [GeneratedAtUtc] IS NULL AND [SummarizedReviewCount] = 0) OR ([SummaryText] IS NOT NULL AND [ContentFingerprint] IS NOT NULL AND [Model] IS NOT NULL AND [GeneratedAtUtc] IS NOT NULL AND [SummarizedReviewCount] > 0)");
                    table.CheckConstraint("CK_ListingReviewSummaries_Counts", "[SummarizedReviewCount] >= 0 AND [AttemptsInWindow] >= 0");
                    table.ForeignKey(
                        name: "FK_ListingReviewSummaries_Listings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "Listings",
                        principalColumn: "ListingId",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <summary>
        /// Removes only the derived cache; original listings, reviews and rental history remain unchanged.
        /// </summary>
        /// <param name="migrationBuilder">The migration operation builder.</param>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListingReviewSummaries");
        }
    }
}
