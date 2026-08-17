-- Day-7 Azure SQL Task 3: Set Operations
--
-- T-SQL port of Day-7/sql/set-operations.sql. EXCEPT / INTERSECT / UNION
-- syntax is identical between SQLite and T-SQL; only table/column
-- definitions changed (see schema.sql). Same three business questions,
-- same operator choices, same reasoning as the local version.

-- ============================================================
-- Q1. Authors who have quotes but no tags.
--
-- EXCEPT: everything in "all authors with a quote" that is NOT in
-- "authors with at least one tagged quote" - a straight set difference.
-- ============================================================
SELECT DISTINCT Author
FROM Quotes
WHERE IsDeleted = 0

EXCEPT

SELECT DISTINCT q.Author
FROM Quotes q
INNER JOIN QuoteTags qt ON qt.QuoteId = q.Id
WHERE q.IsDeleted = 0

ORDER BY Author;


-- ============================================================
-- Q2. Authors who appear in BOTH the "classic" and "modern" sets.
--
-- INTERSECT: only the authors common to both category lists.
-- ============================================================
SELECT Author
FROM AuthorCategories
WHERE Category = 'classic'

INTERSECT

SELECT Author
FROM AuthorCategories
WHERE Category = 'modern'

ORDER BY Author;


-- ============================================================
-- Q3. Combined distinct tag list across the "classic" and "modern"
--     author categories.
--
-- UNION (not UNION ALL): the requirement is a *distinct* tag list, and the
-- two categories share some tags (e.g. "courage", "education" appear in
-- both classic- and modern-authored quotes), so UNION ALL would double
-- count the overlap. UNION discards those duplicates.
-- ============================================================
SELECT t.Name
FROM Tags t
INNER JOIN QuoteTags qt ON qt.TagId = t.Id
INNER JOIN Quotes q ON q.Id = qt.QuoteId
INNER JOIN AuthorCategories ac ON ac.Author = q.Author AND ac.Category = 'classic'
WHERE q.IsDeleted = 0

UNION

SELECT t.Name
FROM Tags t
INNER JOIN QuoteTags qt ON qt.TagId = t.Id
INNER JOIN Quotes q ON q.Id = qt.QuoteId
INNER JOIN AuthorCategories ac ON ac.Author = q.Author AND ac.Category = 'modern'
WHERE q.IsDeleted = 0

ORDER BY Name;
