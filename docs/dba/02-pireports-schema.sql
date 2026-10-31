IF OBJECT_ID(N'[reports].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'reports') IS NULL EXEC(N'CREATE SCHEMA [reports];');
    CREATE TABLE [reports].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [reports].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027182055_InitialCreate'
)
BEGIN
    IF SCHEMA_ID(N'reports') IS NULL EXEC(N'CREATE SCHEMA [reports];');
END;

IF NOT EXISTS (
    SELECT * FROM [reports].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027182055_InitialCreate'
)
BEGIN
    CREATE TABLE [reports].[CaptureDefinitions] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(max) NULL,
        [Enabled] bit NOT NULL,
        [DisplayUrl] nvarchar(2000) NOT NULL,
        [StartTime] nvarchar(100) NULL,
        [EndTime] nvarchar(100) NULL,
        [Asset] nvarchar(1000) NULL,
        [HideToolbar] bit NOT NULL,
        [HideTimebar] bit NOT NULL,
        [Format] nvarchar(10) NOT NULL,
        [ViewportWidth] int NOT NULL,
        [ViewportHeight] int NOT NULL,
        [Landscape] bit NOT NULL,
        [SettleSeconds] int NOT NULL,
        [TimeoutSeconds] int NOT NULL,
        [Cron] nvarchar(100) NULL,
        [TimeZoneId] nvarchar(100) NOT NULL,
        [EmailRecipients] nvarchar(4000) NULL,
        [EmailSubject] nvarchar(500) NULL,
        [AttachMaxMb] int NOT NULL,
        [SharePath] nvarchar(1000) NULL,
        [FileNamePattern] nvarchar(200) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(200) NULL,
        CONSTRAINT [PK_CaptureDefinitions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [reports].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027182055_InitialCreate'
)
BEGIN
    CREATE TABLE [reports].[SmtpSettings] (
        [Id] int NOT NULL,
        [Host] nvarchar(300) NOT NULL,
        [Port] int NOT NULL,
        [Security] nvarchar(30) NOT NULL,
        [Username] nvarchar(300) NULL,
        [ProtectedPassword] nvarchar(max) NULL,
        [FromAddress] nvarchar(300) NOT NULL,
        [FromName] nvarchar(200) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [UpdatedBy] nvarchar(200) NULL,
        CONSTRAINT [PK_SmtpSettings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [reports].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027182055_InitialCreate'
)
BEGIN
    CREATE TABLE [reports].[ReportRuns] (
        [Id] bigint NOT NULL IDENTITY,
        [DefinitionId] int NOT NULL,
        [Trigger] nvarchar(20) NOT NULL,
        [RequestedBy] nvarchar(200) NULL,
        [Status] nvarchar(20) NOT NULL,
        [StartedAt] datetimeoffset NOT NULL,
        [FinishedAt] datetimeoffset NULL,
        [OutputPath] nvarchar(1000) NULL,
        [FileName] nvarchar(300) NULL,
        [OutputBytes] bigint NULL,
        [Warnings] nvarchar(max) NULL,
        [Error] nvarchar(max) NULL,
        CONSTRAINT [PK_ReportRuns] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReportRuns_CaptureDefinitions_DefinitionId] FOREIGN KEY ([DefinitionId]) REFERENCES [reports].[CaptureDefinitions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [reports].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027182055_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CaptureDefinitions_Name] ON [reports].[CaptureDefinitions] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [reports].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027182055_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ReportRuns_DefinitionId_StartedAt] ON [reports].[ReportRuns] ([DefinitionId], [StartedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [reports].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027182055_InitialCreate'
)
BEGIN
    INSERT INTO [reports].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261027182055_InitialCreate', N'10.0.14');
END;

COMMIT;
GO

