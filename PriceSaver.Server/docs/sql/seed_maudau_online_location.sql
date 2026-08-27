-- Idempotent seed: single Maudau online-only store location.
-- Maudau has no physical stores and no IStoreLocationProvider / refresh endpoint.
-- StoreType.Maudau = 2

IF NOT EXISTS (
    SELECT 1
    FROM [dbo].[StoreLocations]
    WHERE [StoreType] = 2
      AND [IsOnline] = 1
      AND [LocationName] = N'Maudau Online'
      AND [ExternalId] IS NULL
)
BEGIN
    INSERT INTO [dbo].[StoreLocations] (
        [Id],
        [LocationName],
        [Latitude],
        [Longitude],
        [StoreType],
        [IsOnline],
        [ExternalId],
        [LastRefreshedAt]
    )
    VALUES (
        NEWID(),
        N'Maudau Online',
        NULL,
        NULL,
        2,
        1,
        NULL,
        NULL
    );
END
GO
