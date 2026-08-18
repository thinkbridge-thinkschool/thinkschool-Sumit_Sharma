-- Day-8 Azure SQL: write-cost.sql
--
-- Demonstrates the write-side cost of indexes ("indexes are a tax on
-- writes") without touching the 100,000-row QuoteEvents benchmark table.
-- Two throwaway tables, identical shape to QuoteEvents, are created:
--   WriteCost_NoIndex - a heap, no indexes at all.
--   WriteCost_Indexed - clustered PK on Id + the same two non-clustered
--                       indexes as QuoteEvents (AuthorId; CreatedAt
--                       covering AuthorId, EventType).
-- The same deterministic 5,000-row batch (rows 1..5000 of the same
-- generator used in seed.sql) is inserted into each, with
-- SET STATISTICS TIME/IO ON, so the extra work SQL Server does to
-- maintain three indexes vs. zero indexes is directly comparable.
-- Both tables are dropped at the end - this script is self-contained and
-- leaves no extra objects behind.

IF OBJECT_ID('dbo.WriteCost_NoIndex', 'U') IS NOT NULL DROP TABLE dbo.WriteCost_NoIndex;
IF OBJECT_ID('dbo.WriteCost_Indexed', 'U') IS NOT NULL DROP TABLE dbo.WriteCost_Indexed;
GO

CREATE TABLE dbo.WriteCost_NoIndex (
    Id        INT IDENTITY(1,1) NOT NULL,
    AuthorId  INT NOT NULL,
    EventType NVARCHAR(20) NOT NULL,
    Status    NVARCHAR(20) NOT NULL,
    CreatedAt DATETIME2(0) NOT NULL,
    Payload   NVARCHAR(500) NOT NULL
);
GO

CREATE TABLE dbo.WriteCost_Indexed (
    Id        INT IDENTITY(1,1) NOT NULL,
    AuthorId  INT NOT NULL,
    EventType NVARCHAR(20) NOT NULL,
    Status    NVARCHAR(20) NOT NULL,
    CreatedAt DATETIME2(0) NOT NULL,
    Payload   NVARCHAR(500) NOT NULL,
    CONSTRAINT PK_WriteCost_Indexed PRIMARY KEY CLUSTERED (Id)
);
GO

CREATE NONCLUSTERED INDEX IX_WriteCost_Indexed_AuthorId
    ON dbo.WriteCost_Indexed (AuthorId);
GO

CREATE NONCLUSTERED INDEX IX_WriteCost_Indexed_CreatedAt
    ON dbo.WriteCost_Indexed (CreatedAt)
    INCLUDE (AuthorId, EventType);
GO

SET STATISTICS IO ON;
SET STATISTICS TIME ON;

;WITH Tally AS (
    SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_objects AS a
    CROSS JOIN sys.all_objects AS b
)
INSERT INTO dbo.WriteCost_NoIndex (AuthorId, EventType, Status, CreatedAt, Payload)
SELECT
    (n % 50) + 1,
    CASE n % 4 WHEN 0 THEN 'View' WHEN 1 THEN 'Like' WHEN 2 THEN 'Share' ELSE 'Comment' END,
    CASE WHEN n % 10 = 0 THEN 'Draft' WHEN n % 10 = 1 THEN 'Archived' ELSE 'Published' END,
    DATEADD(MINUTE, n * 15, CAST('2023-01-01T00:00:00' AS DATETIME2(0))),
    REPLICATE('x', 100) + CAST(n AS NVARCHAR(20))
FROM Tally;
GO

;WITH Tally AS (
    SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_objects AS a
    CROSS JOIN sys.all_objects AS b
)
INSERT INTO dbo.WriteCost_Indexed (AuthorId, EventType, Status, CreatedAt, Payload)
SELECT
    (n % 50) + 1,
    CASE n % 4 WHEN 0 THEN 'View' WHEN 1 THEN 'Like' WHEN 2 THEN 'Share' ELSE 'Comment' END,
    CASE WHEN n % 10 = 0 THEN 'Draft' WHEN n % 10 = 1 THEN 'Archived' ELSE 'Published' END,
    DATEADD(MINUTE, n * 15, CAST('2023-01-01T00:00:00' AS DATETIME2(0))),
    REPLICATE('x', 100) + CAST(n AS NVARCHAR(20))
FROM Tally;

SET STATISTICS TIME OFF;
SET STATISTICS IO OFF;
GO

DROP TABLE dbo.WriteCost_NoIndex;
DROP TABLE dbo.WriteCost_Indexed;
GO
