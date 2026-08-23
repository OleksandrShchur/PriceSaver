-- Incremental migration: user location columns + StoreLocations table
-- Assumes baseline schema from schema.sql is already applied.

ALTER TABLE [dbo].[Users] ADD
    [Latitude] DECIMAL(9,6) NULL,
    [Longitude] DECIMAL(9,6) NULL,
    [LocationName] NVARCHAR(500) NULL,
    [LocationUpdatedAt] DATETIME2 NULL;
GO

CREATE TABLE [dbo].[StoreLocations] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [LocationName] NVARCHAR(500) NOT NULL,
    [Latitude] DECIMAL(9,6) NULL,
    [Longitude] DECIMAL(9,6) NULL,
    [StoreType] INT NOT NULL,
    [IsOnline] BIT NOT NULL DEFAULT 0,
    CONSTRAINT [CK_StoreLocations_OnlineOrCoordinates] CHECK (
        [IsOnline] = 1 OR ([Latitude] IS NOT NULL AND [Longitude] IS NOT NULL)
    )
);
GO

CREATE NONCLUSTERED INDEX [IX_StoreLocations_StoreType]
    ON [dbo].[StoreLocations] ([StoreType]);
GO
