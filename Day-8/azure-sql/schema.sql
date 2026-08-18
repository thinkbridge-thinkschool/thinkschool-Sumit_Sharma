-- Day-8 Azure SQL: schema.sql
--
-- Exercise table for Task 1 (clustered vs non-clustered indexes).
-- This is a Day-8-only table, created specifically for this benchmark; it
-- does not exist in the real QuotesApi schema and is unrelated to the
-- Day-7 Quotes / QuotesTimeline / Tags tables (Day-7 was not modified).
--
-- QuoteEvents simulates an interaction-log table for a quotes app: every
-- time a quote is viewed/liked/shared/commented on, one row is recorded.
-- This shape is deliberately chosen because it gives three columns with
-- three different index-worthiness profiles:
--
--   Id        - surrogate key, ever-increasing (IDENTITY). Good clustered
--               index candidate: sequential inserts land at the end of the
--               table, avoiding page splits, and point lookups by Id
--               (e.g. "get this event by its Id") are a common access
--               pattern for a log/event table.
--   AuthorId  - moderate cardinality (50 distinct values across 100k rows,
--               i.e. ~2% of rows per value). Good non-clustered index
--               candidate: selective enough that an index seek + key
--               lookup beats scanning the whole table.
--   EventType - low cardinality (4 distinct values, ~25% of rows per
--               value). Deliberately left UNINDEXED in this exercise to
--               illustrate that indexing a low-selectivity column rarely
--               pays for itself - the optimizer would still scan a large
--               fraction of the table either way.
--   Status    - low-medium cardinality (3 distinct values), also left
--               unindexed for the same reason as EventType.
--   CreatedAt - high cardinality, naturally ordered timestamp spanning
--               ~2.85 years. Good non-clustered index candidate for range
--               queries ("events in the last 30 days") and for ORDER BY.
--   Payload   - filler text column, simulates realistic row width so page
--               counts (and therefore logical reads) are meaningful.
--
-- The table is created as a HEAP (no primary key, no clustered index) so
-- the baseline queries in baseline-queries.sql measure genuinely
-- unindexed access paths. indexes.sql later adds the clustered primary
-- key and the two non-clustered indexes.

CREATE TABLE dbo.QuoteEvents (
    Id        INT IDENTITY(1,1) NOT NULL,
    AuthorId  INT NOT NULL,
    EventType NVARCHAR(20) NOT NULL,
    Status    NVARCHAR(20) NOT NULL,
    CreatedAt DATETIME2(0) NOT NULL,
    Payload   NVARCHAR(500) NOT NULL
);
GO
