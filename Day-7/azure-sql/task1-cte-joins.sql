-- Day-7 Azure SQL Task 1: CTEs and Joins
--
-- T-SQL port of Day-7/sql/author-quote-report.sql. Same logical approach:
-- a CTE aggregates per-author stats, then a plain INNER JOIN (no correlated
-- subquery in the SELECT list) pulls back the most recent quote's text.
--
-- "Most recent" is still defined by the highest Id (insertion order),
-- because the Quotes table (see schema.sql) has no timestamp column - this
-- matches the real production schema, not an invented column.
--
-- Soft-deleted quotes (IsDeleted = 1) are excluded from both the count and
-- the "most recent" determination.
--
-- LIMIT 10 (SQLite) -> TOP 10 (T-SQL).

WITH AuthorStats AS (
    SELECT
        Author,
        COUNT(*) AS QuoteCount,
        MAX(Id)  AS LatestQuoteId
    FROM Quotes
    WHERE IsDeleted = 0
    GROUP BY Author
)
SELECT TOP 10
    stats.Author       AS Author,
    stats.QuoteCount    AS QuoteCount,
    latest.Text         AS MostRecentQuote,
    latest.Id           AS MostRecentQuoteId
FROM AuthorStats AS stats
INNER JOIN Quotes AS latest
    ON latest.Id = stats.LatestQuoteId
ORDER BY stats.Author;
