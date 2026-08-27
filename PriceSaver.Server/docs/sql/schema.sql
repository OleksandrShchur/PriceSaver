-- SQL schema for PriceSaver

CREATE TABLE [dbo].[Users] (
    [TelegramId] BIGINT NOT NULL PRIMARY KEY,
    [Username] NVARCHAR(100) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [Latitude] DECIMAL(9,6) NULL,
    [Longitude] DECIMAL(9,6) NULL,
    [LocationName] NVARCHAR(500) NULL,
    [LocationUpdatedAt] DATETIME2 NULL,
    [ConversationState] NVARCHAR(32) NOT NULL DEFAULT N'None',
    [ConversationPayload] NVARCHAR(2000) NULL
);

CREATE TABLE [dbo].[Subscriptions] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [UserId] BIGINT NOT NULL,
    [ProductUrl] NVARCHAR(2000) NOT NULL,
    [StoreType] INT NOT NULL,
    [ProductName] NVARCHAR(500) NULL,
    [CurrentPrice] DECIMAL(18,2) NOT NULL,
    [LastCheckedDate] DATETIME2 NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [NotifyOnIncrease] BIT NOT NULL DEFAULT 0,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [UQ_Subscriptions_UserId_ProductUrl] UNIQUE ([UserId], [ProductUrl])
);

CREATE TABLE [dbo].[PriceHistories] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [SubscriptionId] UNIQUEIDENTIFIER NOT NULL,
    [Price] DECIMAL(18,2) NOT NULL,
    [CheckedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

CREATE TABLE [dbo].[StoreLocations] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [LocationName] NVARCHAR(500) NOT NULL,
    [Latitude] DECIMAL(9,6) NULL,
    [Longitude] DECIMAL(9,6) NULL,
    [StoreType] INT NOT NULL,
    [IsOnline] BIT NOT NULL DEFAULT 0,
    [ExternalId] NVARCHAR(100) NULL,
    [LastRefreshedAt] DATETIME2 NULL,
    CONSTRAINT [CK_StoreLocations_OnlineOrCoordinates] CHECK (
        [IsOnline] = 1 OR ([Latitude] IS NOT NULL AND [Longitude] IS NOT NULL)
    )
);

CREATE NONCLUSTERED INDEX [IX_StoreLocations_StoreType]
    ON [dbo].[StoreLocations] ([StoreType]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_StoreLocations_StoreType_ExternalId]
    ON [dbo].[StoreLocations] ([StoreType], [ExternalId])
    WHERE [ExternalId] IS NOT NULL;
