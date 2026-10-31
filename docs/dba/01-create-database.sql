-- PI Reports: create the database, the service account's login and its permissions.
--
-- Run in SSMS with Query > SQLCMD Mode turned on, once per environment.
-- Change the three values below first.

:setvar DatabaseName   "PiReports_Test"
:setvar ServiceAccount "CORP\gmsa-pirpt-tst$"
:setvar DataFolder     ""

USE [master];
GO

IF DB_ID(N'$(DatabaseName)') IS NULL
BEGIN
    -- Small database: configuration and run history only. Report files are stored on the app server, not here.
    IF N'$(DataFolder)' = N''
        EXEC (N'CREATE DATABASE [$(DatabaseName)];');
    ELSE
        EXEC (N'CREATE DATABASE [$(DatabaseName)]
            ON (NAME = N''$(DatabaseName)'', FILENAME = N''$(DataFolder)\$(DatabaseName).mdf'', SIZE = 256MB, FILEGROWTH = 64MB)
            LOG ON (NAME = N''$(DatabaseName)_log'', FILENAME = N''$(DataFolder)\$(DatabaseName)_log.ldf'', SIZE = 128MB, FILEGROWTH = 64MB);');

    -- The scheduler updates its tables constantly; SIMPLE keeps the log small. Use FULL if your backup standard requires it.
    EXEC (N'ALTER DATABASE [$(DatabaseName)] SET RECOVERY SIMPLE;');
    -- Keeps portal reads and scheduler writes from blocking each other.
    EXEC (N'ALTER DATABASE [$(DatabaseName)] SET READ_COMMITTED_SNAPSHOT ON;');
    PRINT N'Created database $(DatabaseName)';
END
GO

IF SUSER_ID(N'$(ServiceAccount)') IS NULL
    CREATE LOGIN [$(ServiceAccount)] FROM WINDOWS WITH DEFAULT_DATABASE = [$(DatabaseName)];
GO

USE [$(DatabaseName)];
GO

IF USER_ID(N'$(ServiceAccount)') IS NULL
    CREATE USER [$(ServiceAccount)] FOR LOGIN [$(ServiceAccount)];
GO

-- Read and write only. The DBA creates and upgrades the tables with scripts 02 and 03,
-- and the app runs with PiReports:ApplyMigrationsOnStartup = false.
ALTER ROLE [db_datareader] ADD MEMBER [$(ServiceAccount)];
ALTER ROLE [db_datawriter] ADD MEMBER [$(ServiceAccount)];
GO

-- Alternative: let the app create and upgrade its own tables on startup
-- (ApplyMigrationsOnStartup = true). Uncomment to grant that instead.
-- ALTER ROLE [db_ddladmin] ADD MEMBER [$(ServiceAccount)];
-- GO
