-- Day-8 Azure SQL: task2-covering-index.sql
--
-- Task 2: demonstrate a real covering-index optimization (Key Lookup ->
-- index-only access) against the same dbo.QuoteEvents table used in
-- Task 1. Task 1's three indexes (PK_QuoteEvents clustered on Id;
-- IX_QuoteEvents_AuthorId nonclustered on AuthorId;
-- IX_QuoteEvents_CreatedAt nonclustered on CreatedAt, INCLUDE (AuthorId,
-- EventType)) are left completely untouched by this file - nothing here
-- drops or alters them.
--
-- Design note: a query filtering only on AuthorId (WHERE AuthorId = 23)
-- does NOT reproduce a Key Lookup here, even though IX_QuoteEvents_AuthorId
-- has no INCLUDE columns: AuthorId is only 2% selective (2,000 of 100,000
-- rows), and at that selectivity 2,000 individual Key Lookups (~3 logical
-- reads each, ~6,000 total) cost more than a single Clustered Index Scan
-- (~3,342 logical reads) - so the optimizer correctly picks the scan
-- instead, and there's no lookup to show. This was verified empirically,
-- not assumed - see task2-results notes.
--
-- What DOES reproduce it: a query against a highly selective 1-day slice
-- of CreatedAt (~96 of 100,000 rows, ~0.1%) that also selects `Status` -
-- a column that exists in NEITHER non-clustered index. At that
-- selectivity, seeking the existing (Task 1) IX_QuoteEvents_CreatedAt
-- index for the 96 matching rows and then looking up each one's Status
-- in the clustered index is far cheaper than scanning the whole table,
-- so the optimizer picks exactly that plan - Index Seek -> Key Lookup.

-- =======================================================================
-- STEP 1: the query under test (same query used for BEFORE and AFTER -
-- predicate, columns, and data are identical; only the index set differs)
-- =======================================================================
SET STATISTICS XML ON;

SELECT Id, AuthorId, EventType, Status, CreatedAt
FROM dbo.QuoteEvents
WHERE CreatedAt >= '2025-06-01' AND CreatedAt < '2025-06-02';

SET STATISTICS XML OFF;

-- BEFORE (captured against the state left by Task 1's indexes.sql, i.e.
-- PK_QuoteEvents + IX_QuoteEvents_AuthorId + IX_QuoteEvents_CreatedAt,
-- nothing else): the actual plan is
--   Nested Loops
--     -> Index Seek on IX_QuoteEvents_CreatedAt        (3 logical reads)
--     -> Clustered Index Seek on PK_QuoteEvents (Key Lookup, correlated
--        by Id to the outer row)                       (258 logical reads)
--   TOTAL: 261 actual logical reads, 96 rows returned.
-- IX_QuoteEvents_CreatedAt covers CreatedAt/AuthorId/EventType but NOT
-- Status, so the engine must look up every one of the 96 matching rows
-- in the clustered index just to fetch Status. Full XML captured in
-- results/task2-before.json.

-- =======================================================================
-- STEP 2: covering index (adds Status as an INCLUDE column, so the
-- 5-column SELECT list above is fully satisfied by one non-clustered
-- index with no trip back to the clustered index)
-- =======================================================================
-- Created as a NEW, separately-named index rather than widening Task 1's
-- IX_QuoteEvents_CreatedAt in place. Reasoning: Task 1's README and
-- results/*.json already describe IX_QuoteEvents_CreatedAt's exact
-- key/include list and cite it as the covering index for Task 1's Q3.
-- Altering it here (even if later restored to its original DDL) would
-- make Task 1's committed, already-verified numbers unreproducible for
-- the period this exercise runs, and risks leaving it in the wrong shape
-- if something goes wrong midway. A distinct index carries a small extra
-- write-maintenance cost from having two indexes keyed on CreatedAt at
-- once - documented as a caveat in README.md's "what could break this"
-- section - but keeps Task 1 provably untouched at every point in time.
CREATE NONCLUSTERED INDEX IX_QuoteEvents_CreatedAt_Covering
    ON dbo.QuoteEvents (CreatedAt)
    INCLUDE (AuthorId, EventType, Status);
GO

-- =======================================================================
-- STEP 3: re-run the EXACT SAME query (same predicate, same columns,
-- same data - only the index set changed)
-- =======================================================================
SET STATISTICS XML ON;

SELECT Id, AuthorId, EventType, Status, CreatedAt
FROM dbo.QuoteEvents
WHERE CreatedAt >= '2025-06-01' AND CreatedAt < '2025-06-02';

SET STATISTICS XML OFF;

-- AFTER: the actual plan is a single
--   Index Seek on IX_QuoteEvents_CreatedAt_Covering    (3 logical reads)
-- with NO Nested Loops back to the clustered index, and NO Clustered
-- Index Seek/Key Lookup operator anywhere in the plan at all - every
-- selected column (Id, AuthorId, EventType, Status, CreatedAt) now comes
-- straight out of the non-clustered index (Id rides along for free as
-- the clustering key every non-clustered index carries).
--   TOTAL: 3 actual logical reads, 96 rows returned (same rows as BEFORE).
-- 261 -> 3 logical reads: an 87x reduction, and the Key Lookup operator
-- is gone from the actual plan, not just cheaper. Full XML captured in
-- results/task2-after.json; see README.md "Task 2" section for the full
-- write-up.

-- Verify the new index exists as designed:
SELECT
    i.name,
    i.type_desc,
    (SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
     FROM sys.index_columns ic JOIN sys.columns c
       ON c.object_id = ic.object_id AND c.column_id = ic.column_id
     WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
       AND ic.is_included_column = 0) AS key_columns,
    (SELECT STRING_AGG(c.name, ',')
     FROM sys.index_columns ic JOIN sys.columns c
       ON c.object_id = ic.object_id AND c.column_id = ic.column_id
     WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
       AND ic.is_included_column = 1) AS include_columns
FROM sys.indexes i
JOIN sys.tables t ON t.object_id = i.object_id
WHERE t.name = 'QuoteEvents' AND i.name IS NOT NULL;
GO
