BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121231843_AddRentalApprovalMessageEvents'
)
BEGIN
    DROP INDEX [IX_Messages_RentalRequestId] ON [Messages];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121231843_AddRentalApprovalMessageEvents'
)
BEGIN
    ALTER TABLE [Messages] ADD [RentalEventType] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121231843_AddRentalApprovalMessageEvents'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Messages_RentalRequestId_RentalEventType] ON [Messages] ([RentalRequestId], [RentalEventType]) WHERE [RentalRequestId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121231843_AddRentalApprovalMessageEvents'
)
BEGIN
    EXEC(N'ALTER TABLE [Messages] ADD CONSTRAINT [CK_Messages_RentalEventType] CHECK ([RentalEventType] IN (0, 1) AND ([RentalRequestId] IS NOT NULL OR [RentalEventType] = 0))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121231843_AddRentalApprovalMessageEvents'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261121231843_AddRentalApprovalMessageEvents', N'10.0.14');
END;

COMMIT;
GO

