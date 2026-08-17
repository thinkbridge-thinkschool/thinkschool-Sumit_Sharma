-- Day-7 Task 3: set-operations.sql
--
-- Three business questions, each answered with the set operator that
-- actually matches the requirement. Data source: Quotes (real) plus Tags /
-- QuoteTags / AuthorCategories (Day-7-only exercise tables - see
-- set-operations-seed.sql for how/why they were built).

-- ============================================================
-- Q1. Authors who have quotes but no tags.
--
-- EXCEPT is correct here: we want everything in "all authors with a
-- quote" that is NOT in "authors with at least one tagged quote" - a
-- straight set difference, which is exactly what EXCEPT computes.
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
-- INTERSECT is correct here: we want only the authors common to both
-- category lists, which is the definition of a set intersection.
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
-- UNION (not UNION ALL) is correct here: the requirement is a *distinct*
-- tag list, and the two categories share some tags (e.g. "courage",
-- "education" appear in both classic- and modern-authored quotes), so a
-- plain UNION ALL would double-count the overlap. UNION discards those
-- duplicates.
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
