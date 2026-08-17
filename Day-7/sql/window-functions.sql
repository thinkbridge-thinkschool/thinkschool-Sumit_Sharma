-- Day-7 Task 2: window-functions.sql
--
-- Per author, for each quote: a running count, and the gap in days since
-- that author's previous quote.
--
-- Data source: QuotesTimeline (see window-functions-seed.sql for how it was
-- built and why). The real Quotes table has no date column, so this query
-- deliberately reads from the Day-7-only QuotesTimeline table, which pairs
-- each real Quote row with a clearly-labeled synthetic CreatedAt date.
--
-- Window functions demonstrated, all PARTITION BY Author, ORDER BY CreatedAt:
--   ROW_NUMBER() - a strict 1,2,3... sequence per author, no ties.
--   RANK()       - same idea, but ties in CreatedAt share a rank and the
--                  next rank is skipped (see Albert Einstein, who has two
--                  quotes on the same synthetic date).
--   LAG()        - pulls each author's previous quote's text and date onto
--                  the current row.
--   SUM() OVER (... ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)
--                - a running count of quotes seen so far for that author.
--
-- GapInDays = julianday(CreatedAt) - julianday(previous CreatedAt), i.e.
-- the actual number of calendar days between a quote and the one before it
-- from the same author. NULL for an author's first quote (no previous row).

SELECT
    Author,
    Text                                                            AS Quote,
    QuoteId,
    CreatedAt,
    ROW_NUMBER() OVER (
        PARTITION BY Author
        ORDER BY CreatedAt
    )                                                                AS RowNumber,
    RANK() OVER (
        PARTITION BY Author
        ORDER BY CreatedAt
    )                                                                AS Rank,
    SUM(1) OVER (
        PARTITION BY Author
        ORDER BY CreatedAt
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    )                                                                AS RunningQuoteCount,
    LAG(Text) OVER (
        PARTITION BY Author
        ORDER BY CreatedAt
    )                                                                AS PreviousQuote,
    LAG(CreatedAt) OVER (
        PARTITION BY Author
        ORDER BY CreatedAt
    )                                                                AS PreviousQuoteDate,
    CAST(
        julianday(CreatedAt) - julianday(
            LAG(CreatedAt) OVER (
                PARTITION BY Author
                ORDER BY CreatedAt
            )
        ) AS INTEGER
    )                                                                AS GapInDays
FROM QuotesTimeline
ORDER BY Author, CreatedAt, QuoteId;
