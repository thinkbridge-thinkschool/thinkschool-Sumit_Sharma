-- Day-7 Azure SQL Task 2: Window Functions
--
-- T-SQL port of Day-7/sql/window-functions.sql, against QuotesTimeline
-- (see schema.sql / seed.sql) for the same reason as the local version:
-- the real Quotes table has no date column, so this Day-7-only timeline
-- table supplies the CreatedAt dates needed for day-gap arithmetic.
--
-- Window functions demonstrated, all PARTITION BY Author, ORDER BY CreatedAt:
--   ROW_NUMBER() - strict 1,2,3... sequence per author, no ties.
--   RANK()       - ties in CreatedAt share a rank and the next rank is
--                  skipped (see Albert Einstein, two quotes on the same
--                  synthetic date).
--   LAG()        - previous quote's text and date onto the current row.
--   SUM() OVER (... ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)
--                - running count of quotes seen so far for that author.
--
-- GapInDays: SQLite used julianday(CreatedAt) - julianday(previous
-- CreatedAt). T-SQL equivalent is DATEDIFF(DAY, previous, current) - the
-- number of calendar days between a quote and the one before it from the
-- same author. NULL for an author's first quote (no previous row).

SELECT
    Author,
    Text AS Quote,
    QuoteId,
    CreatedAt,
    ROW_NUMBER() OVER (
        PARTITION BY Author
        ORDER BY CreatedAt, QuoteId
    ) AS RowNumber,
    RANK() OVER (
        PARTITION BY Author
        ORDER BY CreatedAt
    ) AS [Rank],
    SUM(1) OVER (
        PARTITION BY Author
        ORDER BY CreatedAt, QuoteId
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS RunningQuoteCount,
    LAG(Text) OVER (
        PARTITION BY Author
        ORDER BY CreatedAt, QuoteId
    ) AS PreviousQuote,
    LAG(CreatedAt) OVER (
        PARTITION BY Author
        ORDER BY CreatedAt, QuoteId
    ) AS PreviousQuoteDate,
    DATEDIFF(
        DAY,
        LAG(CreatedAt) OVER (
            PARTITION BY Author
            ORDER BY CreatedAt, QuoteId
        ),
        CreatedAt
    ) AS GapInDays
FROM QuotesTimeline
ORDER BY Author, CreatedAt, QuoteId;
