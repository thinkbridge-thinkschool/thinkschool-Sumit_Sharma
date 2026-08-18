-- Day-8 Azure SQL: seed.sql
--
-- Generates 100,000 deterministic rows into dbo.QuoteEvents using a tally
-- (numbers) CTE built from sys.all_objects, instead of 100,000 literal
-- INSERT statements. Every derived value is a pure function of the row
-- number `n`, so re-running this script against a freshly created table
-- always reproduces the exact same 100,000 rows.
--
-- Derivation of each column from n (1..100000):
--   AuthorId  = (n % 50) + 1          -> 50 distinct authors, exactly 2,000
--                                        rows each (uniform distribution).
--   EventType = n % 4                 -> 4 distinct values, 25,000 rows each
--               (View/Like/Share/Comment).
--   Status    = n % 10                -> 3 distinct values, skewed so most
--               rows are 'Published' (80%), some 'Draft' (10%),
--               some 'Archived' (10%) - mimics a realistic status split.
--   CreatedAt = '2023-01-01' + (n * 15) minutes
--                                      -> spreads 100,000 rows across
--                                        1,500,000 minutes (~1,041 days,
--                                        ~2.85 years), so a "last 30 days"
--                                        filter is genuinely selective
--                                        (~30/1041 =~ 2.9% of rows).
--   Payload   = 100 filler characters + the row number, so each row has a
--               realistic (non-trivial) row width without needing random
--               text generation.
--
-- Run this once against dbo.QuoteEvents after schema.sql.

SET NOCOUNT ON;

;WITH Tally AS (
    SELECT TOP (100000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_objects AS a
    CROSS JOIN sys.all_objects AS b
)
INSERT INTO dbo.QuoteEvents (AuthorId, EventType, Status, CreatedAt, Payload)
SELECT
    (n % 50) + 1 AS AuthorId,
    CASE n % 4
        WHEN 0 THEN 'View'
        WHEN 1 THEN 'Like'
        WHEN 2 THEN 'Share'
        ELSE 'Comment'
    END AS EventType,
    CASE
        WHEN n % 10 = 0 THEN 'Draft'
        WHEN n % 10 = 1 THEN 'Archived'
        ELSE 'Published'
    END AS Status,
    DATEADD(MINUTE, n * 15, CAST('2023-01-01T00:00:00' AS DATETIME2(0))) AS CreatedAt,
    REPLICATE('x', 100) + CAST(n AS NVARCHAR(20)) AS Payload
FROM Tally;
GO
