-- SQL schema for PriceSaver

CREATE TABLE [dbo].[Users] (
    [TelegramId] BIGINT NOT NULL PRIMARY KEY,
    [Username] NVARCHAR(100) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
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
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

CREATE TABLE [dbo].[PriceHistories] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [SubscriptionId] UNIQUEIDENTIFIER NOT NULL,
    [Price] DECIMAL(18,2) NOT NULL,
    [CheckedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);
