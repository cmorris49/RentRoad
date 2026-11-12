BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112014946_AddMessagingRetryAndHistoryIndexes'
)
BEGIN
    ALTER TABLE [Messages] ADD [ClientMessageId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112014946_AddMessagingRetryAndHistoryIndexes'
)
BEGIN
    CREATE INDEX [IX_Messages_ConversationId_MessageId] ON [Messages] ([ConversationId], [MessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112014946_AddMessagingRetryAndHistoryIndexes'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Messages_ConversationId_SenderId_ClientMessageId] ON [Messages] ([ConversationId], [SenderId], [ClientMessageId]) WHERE [ClientMessageId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112014946_AddMessagingRetryAndHistoryIndexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261112014946_AddMessagingRetryAndHistoryIndexes', N'10.0.14');
END;

COMMIT;
GO

