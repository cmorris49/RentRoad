IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [AspNetRoles] (
    [Id] nvarchar(450) NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetUsers] (
    [Id] nvarchar(450) NOT NULL,
    [DisplayName] nvarchar(100) NOT NULL,
    [Location] nvarchar(150) NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(128) NOT NULL,
    [ProviderKey] nvarchar(128) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserRoles] (
    [UserId] nvarchar(450) NOT NULL,
    [RoleId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserTokens] (
    [UserId] nvarchar(450) NOT NULL,
    [LoginProvider] nvarchar(128) NOT NULL,
    [Name] nvarchar(128) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [Listings] (
    [ListingId] int NOT NULL IDENTITY,
    [OwnerId] nvarchar(450) NOT NULL,
    [Title] nvarchar(150) NULL,
    [Description] nvarchar(2000) NULL,
    [Category] int NULL,
    [Condition] int NULL,
    [DailyPriceCents] bigint NULL,
    [PickupArea] nvarchar(150) NULL,
    [Latitude] float NULL,
    [Longitude] float NULL,
    [Status] int NOT NULL DEFAULT 0,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UpdatedAtUtc] datetime2 NOT NULL,
    [PublishedAtUtc] datetime2 NULL,
    CONSTRAINT [PK_Listings] PRIMARY KEY ([ListingId]),
    CONSTRAINT [CK_Listings_Category] CHECK ([Category] IN (0, 1, 2, 3, 4, 5)),
    CONSTRAINT [CK_Listings_Condition] CHECK ([Condition] IN (0, 1, 2, 3)),
    CONSTRAINT [CK_Listings_DailyPriceCents] CHECK ([DailyPriceCents] >= 0),
    CONSTRAINT [CK_Listings_PublishedPrice] CHECK ([Status] <> 1 OR ([DailyPriceCents] IS NOT NULL AND [DailyPriceCents] > 0)),
    CONSTRAINT [CK_Listings_Status] CHECK ([Status] IN (0, 1, 2, 3)),
    CONSTRAINT [FK_Listings_AspNetUsers_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [AvailabilityBlocks] (
    [AvailabilityBlockId] int NOT NULL IDENTITY,
    [ListingId] int NOT NULL,
    [StartDate] date NOT NULL,
    [EndDate] date NOT NULL,
    [Reason] nvarchar(250) NULL,
    CONSTRAINT [PK_AvailabilityBlocks] PRIMARY KEY ([AvailabilityBlockId]),
    CONSTRAINT [CK_AvailabilityBlocks_DateRange] CHECK ([StartDate] < [EndDate]),
    CONSTRAINT [FK_AvailabilityBlocks_Listings_ListingId] FOREIGN KEY ([ListingId]) REFERENCES [Listings] ([ListingId]) ON DELETE NO ACTION
);

CREATE TABLE [AvailabilityWindows] (
    [AvailabilityWindowId] int NOT NULL IDENTITY,
    [ListingId] int NOT NULL,
    [StartDate] date NOT NULL,
    [EndDate] date NOT NULL,
    CONSTRAINT [PK_AvailabilityWindows] PRIMARY KEY ([AvailabilityWindowId]),
    CONSTRAINT [CK_AvailabilityWindows_DateRange] CHECK ([StartDate] < [EndDate]),
    CONSTRAINT [FK_AvailabilityWindows_Listings_ListingId] FOREIGN KEY ([ListingId]) REFERENCES [Listings] ([ListingId]) ON DELETE NO ACTION
);

CREATE TABLE [Conversations] (
    [ConversationId] int NOT NULL IDENTITY,
    [ListingId] int NOT NULL,
    [RenterId] nvarchar(450) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_Conversations] PRIMARY KEY ([ConversationId]),
    CONSTRAINT [FK_Conversations_AspNetUsers_RenterId] FOREIGN KEY ([RenterId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Conversations_Listings_ListingId] FOREIGN KEY ([ListingId]) REFERENCES [Listings] ([ListingId]) ON DELETE NO ACTION
);

CREATE TABLE [ListingImages] (
    [ListingImageId] int NOT NULL IDENTITY,
    [ListingId] int NOT NULL,
    [ContainerName] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [BlobName] nvarchar(1024) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [OriginalFileName] nvarchar(255) NULL,
    [ContentType] nvarchar(100) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [AltText] nvarchar(250) NULL,
    [IsPrimary] bit NOT NULL DEFAULT CAST(0 AS bit),
    [DisplayOrder] int NOT NULL DEFAULT 0,
    [CreatedAtUtc] datetime2 NOT NULL,
    [BlobNameHash] AS CONVERT(binary(32), HASHBYTES('SHA2_256', [BlobName])) PERSISTED,
    CONSTRAINT [PK_ListingImages] PRIMARY KEY ([ListingImageId]),
    CONSTRAINT [CK_ListingImages_SizeBytes] CHECK ([SizeBytes] >= 0),
    CONSTRAINT [FK_ListingImages_Listings_ListingId] FOREIGN KEY ([ListingId]) REFERENCES [Listings] ([ListingId]) ON DELETE NO ACTION
);

CREATE TABLE [RentalRequests] (
    [RentalRequestId] int NOT NULL IDENTITY,
    [ListingId] int NOT NULL,
    [RenterId] nvarchar(450) NOT NULL,
    [StartDate] date NOT NULL,
    [EndDate] date NOT NULL,
    [PickupTime] time NOT NULL,
    [ReturnTime] time NOT NULL,
    [DailyPriceAtRequestCents] bigint NOT NULL,
    [TotalPriceCents] bigint NOT NULL,
    [Status] int NOT NULL DEFAULT 0,
    [CreatedAtUtc] datetime2 NOT NULL,
    [DecisionAtUtc] datetime2 NULL,
    [CompletedAtUtc] datetime2 NULL,
    CONSTRAINT [PK_RentalRequests] PRIMARY KEY ([RentalRequestId]),
    CONSTRAINT [CK_RentalRequests_DailyPriceAtRequestCents] CHECK ([DailyPriceAtRequestCents] >= 0),
    CONSTRAINT [CK_RentalRequests_DateRange] CHECK ([StartDate] < [EndDate]),
    CONSTRAINT [CK_RentalRequests_Status] CHECK ([Status] IN (0, 1, 2, 3, 4, 5)),
    CONSTRAINT [CK_RentalRequests_TotalPriceCents] CHECK ([TotalPriceCents] >= 0),
    CONSTRAINT [FK_RentalRequests_AspNetUsers_RenterId] FOREIGN KEY ([RenterId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RentalRequests_Listings_ListingId] FOREIGN KEY ([ListingId]) REFERENCES [Listings] ([ListingId]) ON DELETE NO ACTION
);

CREATE TABLE [UserActivities] (
    [UserActivityId] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ListingId] int NULL,
    [Category] int NULL,
    [ActivityType] int NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_UserActivities] PRIMARY KEY ([UserActivityId]),
    CONSTRAINT [CK_UserActivities_ActivityType] CHECK ([ActivityType] IN (0, 1)),
    CONSTRAINT [CK_UserActivities_Category] CHECK ([Category] IN (0, 1, 2, 3, 4, 5)),
    CONSTRAINT [FK_UserActivities_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_UserActivities_Listings_ListingId] FOREIGN KEY ([ListingId]) REFERENCES [Listings] ([ListingId]) ON DELETE NO ACTION
);

CREATE TABLE [Messages] (
    [MessageId] int NOT NULL IDENTITY,
    [ConversationId] int NOT NULL,
    [SenderId] nvarchar(450) NOT NULL,
    [Content] nvarchar(2000) NOT NULL,
    [SentAtUtc] datetime2 NOT NULL,
    [ReadAtUtc] datetime2 NULL,
    CONSTRAINT [PK_Messages] PRIMARY KEY ([MessageId]),
    CONSTRAINT [FK_Messages_AspNetUsers_SenderId] FOREIGN KEY ([SenderId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Messages_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([ConversationId]) ON DELETE NO ACTION
);

CREATE TABLE [Reviews] (
    [ReviewId] int NOT NULL IDENTITY,
    [RentalRequestId] int NOT NULL,
    [Rating] int NOT NULL,
    [Comment] nvarchar(2000) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_Reviews] PRIMARY KEY ([ReviewId]),
    CONSTRAINT [CK_Reviews_Rating] CHECK ([Rating] BETWEEN 1 AND 5),
    CONSTRAINT [FK_Reviews_RentalRequests_RentalRequestId] FOREIGN KEY ([RentalRequestId]) REFERENCES [RentalRequests] ([RentalRequestId]) ON DELETE NO ACTION
);

CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;

CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);

CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);

CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;

CREATE INDEX [IX_AvailabilityBlocks_ListingId_StartDate_EndDate] ON [AvailabilityBlocks] ([ListingId], [StartDate], [EndDate]);

CREATE INDEX [IX_AvailabilityWindows_ListingId_StartDate_EndDate] ON [AvailabilityWindows] ([ListingId], [StartDate], [EndDate]);

CREATE UNIQUE INDEX [IX_Conversations_ListingId_RenterId] ON [Conversations] ([ListingId], [RenterId]);

CREATE INDEX [IX_Conversations_RenterId] ON [Conversations] ([RenterId]);

CREATE UNIQUE INDEX [IX_ListingImages_ContainerName_BlobNameHash] ON [ListingImages] ([ContainerName], [BlobNameHash]);

CREATE UNIQUE INDEX [IX_ListingImages_ListingId] ON [ListingImages] ([ListingId]) WHERE [IsPrimary] = 1;

CREATE INDEX [IX_ListingImages_ListingId_IsPrimary] ON [ListingImages] ([ListingId], [IsPrimary]);

CREATE INDEX [IX_Listings_OwnerId_Status] ON [Listings] ([OwnerId], [Status]);

CREATE INDEX [IX_Listings_PickupArea] ON [Listings] ([PickupArea]);

CREATE INDEX [IX_Listings_Status_Category] ON [Listings] ([Status], [Category]);

CREATE INDEX [IX_Messages_ConversationId_SentAtUtc] ON [Messages] ([ConversationId], [SentAtUtc]);

CREATE INDEX [IX_Messages_SenderId] ON [Messages] ([SenderId]);

CREATE INDEX [IX_RentalRequests_ListingId_Status_StartDate_EndDate] ON [RentalRequests] ([ListingId], [Status], [StartDate], [EndDate]);

CREATE INDEX [IX_RentalRequests_RenterId_Status] ON [RentalRequests] ([RenterId], [Status]);

CREATE UNIQUE INDEX [IX_Reviews_RentalRequestId] ON [Reviews] ([RentalRequestId]);

CREATE INDEX [IX_UserActivities_ListingId_ActivityType] ON [UserActivities] ([ListingId], [ActivityType]);

CREATE INDEX [IX_UserActivities_UserId_CreatedAtUtc] ON [UserActivities] ([UserId], [CreatedAtUtc]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261108065139_InitialRentRoadSqlServer', N'10.0.14');

COMMIT;
GO

