-- Day-7 Task 1: Joins and CTEs at depth
--
-- For each author, return their quote count and their most recent quote,
-- in one statement, using a CTE and a plain (non-correlated) join.
--
-- "Most recent" is defined by the highest Id (insertion order), since the
-- real Quotes schema has no timestamp column - Id is the only recency
-- signal that actually exists in the data.
--
-- Soft-deleted quotes (IsDeleted = 1) are excluded from both the count and
-- the "most recent" determination, matching Quote.MarkDeleted() semantics
-- in QuotesApi/Models/Quote.cs.

WITH AuthorStats AS (
    SELECT
        Author,
        COUNT(*)  AS QuoteCount,
        MAX(Id)   AS LatestQuoteId
    FROM Quotes
    WHERE IsDeleted = 0
    GROUP BY Author
)
SELECT
    stats.Author                AS Author,
    stats.QuoteCount             AS QuoteCount,
    latest.Text                  AS MostRecentQuote,
    latest.Id                    AS MostRecentQuoteId
FROM AuthorStats AS stats
INNER JOIN Quotes AS latest
    ON latest.Id = stats.LatestQuoteId
ORDER BY stats.Author
LIMIT 10;
