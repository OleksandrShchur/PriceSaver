-- Incremental migration: conversation state for multi-step Telegram flows
-- Assumes 001_add_user_and_store_locations.sql (or updated schema.sql) is applied.

ALTER TABLE [dbo].[Users] ADD
    [ConversationState] NVARCHAR(32) NOT NULL CONSTRAINT [DF_Users_ConversationState] DEFAULT N'None',
    [ConversationPayload] NVARCHAR(2000) NULL;
GO
