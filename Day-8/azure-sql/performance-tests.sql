-- Day-8 Azure SQL: performance-tests.sql
--
-- The three representative queries used to measure before/after index
-- behavior on dbo.QuoteEvents. Each query is run twice against the live
-- database in this exercise:
--   (a) BEFORE - table is a heap (schema.sql + seed.sql only)
--   (b) AFTER  - table has the clustered PK and, for Q2/Q3, the matching
--                non-clustered index from indexes.sql
--
-- In SSMS / Azure Data Studio this would be run with "Include Actual
-- Execution Plan" turned on. This environment has no SSMS/ADS GUI
-- available (Linux dev box, no sqlcmd/mssql-cli installed either), so
-- the actual plan and STATISTICS IO output were captured programmatically
-- instead - see Day-8/README.md "Verification" for exactly how.

SET STATISTICS IO ON;
SET STATISTICS XML ON; -- returns the real "actual" execution plan (with
                        -- runtime row counts/reads), the same information
                        -- SSMS's graphical plan is built from.

-- ---------------------------------------------------------------------
-- Q1: point lookup by Id
-- ---------------------------------------------------------------------
-- BEFORE the clustered index: no way to seek on Id at all -> full scan.
-- AFTER the clustered index: Id is the clustering key -> direct seek.
SELECT Id, AuthorId, EventType, Status, CreatedAt
FROM dbo.QuoteEvents
WHERE Id = 55555;

-- ---------------------------------------------------------------------
-- Q2: equality filter by AuthorId (50 distinct values, ~2% selectivity)
-- ---------------------------------------------------------------------
-- BEFORE any index on AuthorId: full scan of the table/clustered index.
-- AFTER IX_QuoteEvents_AuthorId: non-clustered index seek + key lookup
-- (the index isn't covering for EventType/CreatedAt).
SELECT Id, AuthorId, EventType, CreatedAt
FROM dbo.QuoteEvents
WHERE AuthorId = 23;

-- ---------------------------------------------------------------------
-- Q3: date-range filter + ORDER BY (30-day window, ~2.9% selectivity)
-- ---------------------------------------------------------------------
-- BEFORE any index on CreatedAt: full scan + explicit sort.
-- AFTER IX_QuoteEvents_CreatedAt (covering, INCLUDE AuthorId, EventType):
-- non-clustered index seek, no key lookup, no separate sort operator
-- (index key order already satisfies ORDER BY CreatedAt).
SELECT Id, AuthorId, EventType, CreatedAt
FROM dbo.QuoteEvents
WHERE CreatedAt >= '2025-06-01' AND CreatedAt < '2025-07-01'
ORDER BY CreatedAt;

SET STATISTICS XML OFF;
SET STATISTICS IO OFF;
