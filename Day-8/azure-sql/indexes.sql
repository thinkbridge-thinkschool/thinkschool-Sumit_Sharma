-- Day-8 Azure SQL: indexes.sql
--
-- Adds exactly one clustered index and two non-clustered indexes to
-- dbo.QuoteEvents. Run baseline-queries.sql's "BEFORE" section first (on
-- the heap table from schema.sql/seed.sql) so there is a genuine
-- unindexed measurement to compare against.

-- ---------------------------------------------------------------------
-- 1. Clustered index (as the table's primary key)
-- ---------------------------------------------------------------------
-- Why Id: Id is an ever-increasing IDENTITY surrogate key.
--   - Point lookups by Id ("fetch this one event") are the single most
--     common access pattern for a log/event-style table, and a clustered
--     index turns that into a direct B-tree seek to the exact data page.
--   - Because Id only ever increases, every insert lands at the logical
--     end of the clustered index, so there is no random-page insert
--     pattern and (outside of very rare full-page splits at the end of a
--     page) no clustered-key-driven page splits - cheap to maintain even
--     though every non-clustered index carries Id as its row locator.
--   - This is NOT chosen "because Id is conventional": CreatedAt was also
--     considered (naturally near-sequential too), but Id is unique on its
--     own (no duplicate-key tie-breaking needed) and matches the actual
--     query pattern exercised below (GetById-style lookup).
ALTER TABLE dbo.QuoteEvents
    ADD CONSTRAINT PK_QuoteEvents PRIMARY KEY CLUSTERED (Id);
GO

-- ---------------------------------------------------------------------
-- 2. Non-clustered index #1: equality filter by AuthorId
-- ---------------------------------------------------------------------
-- Supports: "show all events for a given author"
--   SELECT Id, AuthorId, EventType, CreatedAt
--   FROM dbo.QuoteEvents
--   WHERE AuthorId = @AuthorId;
-- AuthorId has 50 distinct values across 100,000 rows (~2% of rows per
-- value), which is selective enough for the optimizer to prefer a
-- non-clustered index seek over scanning the whole table. The index does
-- NOT include EventType/CreatedAt, so this query needs a key lookup back
-- into the clustered index for those columns - included deliberately so
-- the "seek + key lookup" pattern (and its cost vs. a covering index) is
-- visible in the execution plan and STATISTICS IO output.
CREATE NONCLUSTERED INDEX IX_QuoteEvents_AuthorId
    ON dbo.QuoteEvents (AuthorId);
GO

-- ---------------------------------------------------------------------
-- 3. Non-clustered index #2: range filter + sort by CreatedAt (covering)
-- ---------------------------------------------------------------------
-- Supports: "show recent events in a date range, ordered by time"
--   SELECT Id, AuthorId, EventType, CreatedAt
--   FROM dbo.QuoteEvents
--   WHERE CreatedAt >= @From AND CreatedAt < @To
--   ORDER BY CreatedAt;
-- CreatedAt spans ~2.85 years, so a 30-day window is ~2.9% of rows -
-- selective enough for a seek, and the index's natural key order also
-- satisfies ORDER BY CreatedAt without a separate sort. AuthorId and
-- EventType are added as INCLUDE columns (not key columns) specifically
-- to make this index COVERING for the query above: SQL Server can answer
-- it entirely from the non-clustered index, with no key lookup into the
-- clustered index at all (Id is already present as the clustering key
-- carried by every non-clustered index row).
CREATE NONCLUSTERED INDEX IX_QuoteEvents_CreatedAt
    ON dbo.QuoteEvents (CreatedAt)
    INCLUDE (AuthorId, EventType);
GO

-- Deliberately NOT indexed: EventType (4 distinct values, ~25% of rows
-- each) and Status (effectively 3 distinct values). Both are too low in
-- selectivity for an index seek to beat a scan for most predicates - the
-- optimizer would still have to visit a large fraction of the table's
-- rows/pages either way, so an index on either column would mostly add
-- write overhead and storage without a matching read benefit. This is
-- demonstrated (not just asserted) in performance-tests.sql.
