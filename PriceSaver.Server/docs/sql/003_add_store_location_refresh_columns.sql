-- Incremental migration: ExternalId + LastRefreshedAt for store location refresh upserts
-- Assumes 001_add_user_and_store_locations.sql (or updated schema.sql) is applied.

ALTER TABLE [dbo].[StoreLocations] ADD
    [ExternalId] NVARCHAR(100) NULL,
    [LastRefreshedAt] DATETIME2 NULL;
GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_StoreLocations_StoreType_ExternalId]
    ON [dbo].[StoreLocations] ([StoreType], [ExternalId])
    WHERE [ExternalId] IS NOT NULL;
GO
