-- PI Reports: quick check after scripts 01-03. Run in the PI Reports database.
SELECT s.name AS [schema], t.name AS [table]
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name IN (N'reports', N'HangFire')
ORDER BY s.name, t.name;

SELECT [MigrationId] FROM [reports].[__EFMigrationsHistory] ORDER BY [MigrationId];
SELECT [Version] AS HangfireSchemaVersion FROM [HangFire].[Schema];

SELECT dp.name AS [user], r.name AS [role]
FROM sys.database_role_members m
JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
JOIN sys.database_principals dp ON dp.principal_id = m.member_principal_id
WHERE dp.type IN ('U', 'G')
ORDER BY dp.name, r.name;
