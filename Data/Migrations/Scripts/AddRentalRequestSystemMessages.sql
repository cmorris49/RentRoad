BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121225931_AddRentalRequestSystemMessages'
)
BEGIN
    ALTER TABLE [RentalRequests] ADD [ClientRequestId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121225931_AddRentalRequestSystemMessages'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Messages]') AND [c].[name] = N'SenderId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Messages] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Messages] ALTER COLUMN [SenderId] nvarchar(450) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121225931_AddRentalRequestSystemMessages'
)
BEGIN
    ALTER TABLE [Messages] ADD [RentalRequestId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121225931_AddRentalRequestSystemMessages'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RentalRequests_RenterId_ClientRequestId] ON [RentalRequests] ([RenterId], [ClientRequestId]) WHERE [ClientRequestId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121225931_AddRentalRequestSystemMessages'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Messages_RentalRequestId] ON [Messages] ([RentalRequestId]) WHERE [RentalRequestId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121225931_AddRentalRequestSystemMessages'
)
BEGIN
    EXEC(N'ALTER TABLE [Messages] ADD CONSTRAINT [CK_Messages_Authorship] CHECK (([RentalRequestId] IS NULL AND [SenderId] IS NOT NULL) OR ([RentalRequestId] IS NOT NULL AND [SenderId] IS NULL AND [ClientMessageId] IS NULL))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121225931_AddRentalRequestSystemMessages'
)
BEGIN
    ALTER TABLE [Messages] ADD CONSTRAINT [FK_Messages_RentalRequests_RentalRequestId] FOREIGN KEY ([RentalRequestId]) REFERENCES [RentalRequests] ([RentalRequestId]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121225931_AddRentalRequestSystemMessages'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261121225931_AddRentalRequestSystemMessages', N'10.0.14');
END;

COMMIT;
GO

