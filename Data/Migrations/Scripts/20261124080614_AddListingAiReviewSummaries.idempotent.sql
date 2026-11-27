BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124080614_AddListingAiReviewSummaries'
)
BEGIN
    CREATE TABLE [ListingReviewSummaries] (
        [ListingId] int NOT NULL,
        [SummaryText] nvarchar(1000) NULL,
        [ContentFingerprint] varchar(64) NULL,
        [Model] nvarchar(120) NULL,
        [GeneratedAtUtc] datetime2 NULL,
        [SummarizedReviewCount] int NOT NULL,
        [GenerationId] uniqueidentifier NULL,
        [GenerationExpiresAtUtc] datetime2 NULL,
        [NextAttemptAtUtc] datetime2 NULL,
        [AttemptWindowStartedAtUtc] datetime2 NULL,
        [AttemptsInWindow] int NOT NULL,
        CONSTRAINT [PK_ListingReviewSummaries] PRIMARY KEY ([ListingId]),
        CONSTRAINT [CK_ListingReviewSummaries_Claim] CHECK (([GenerationId] IS NULL AND [GenerationExpiresAtUtc] IS NULL) OR ([GenerationId] IS NOT NULL AND [GenerationExpiresAtUtc] IS NOT NULL)),
        CONSTRAINT [CK_ListingReviewSummaries_Content] CHECK (([SummaryText] IS NULL AND [ContentFingerprint] IS NULL AND [Model] IS NULL AND [GeneratedAtUtc] IS NULL AND [SummarizedReviewCount] = 0) OR ([SummaryText] IS NOT NULL AND [ContentFingerprint] IS NOT NULL AND [Model] IS NOT NULL AND [GeneratedAtUtc] IS NOT NULL AND [SummarizedReviewCount] > 0)),
        CONSTRAINT [CK_ListingReviewSummaries_Counts] CHECK ([SummarizedReviewCount] >= 0 AND [AttemptsInWindow] >= 0),
        CONSTRAINT [FK_ListingReviewSummaries_Listings_ListingId] FOREIGN KEY ([ListingId]) REFERENCES [Listings] ([ListingId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124080614_AddListingAiReviewSummaries'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261124080614_AddListingAiReviewSummaries', N'10.0.14');
END;

COMMIT;
GO

