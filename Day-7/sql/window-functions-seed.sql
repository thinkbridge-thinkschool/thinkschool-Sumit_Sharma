-- Day-7 Task 2: window-functions-seed.sql
--
-- The real Quotes table (see schema.sql) has no timestamp/date column - it
-- was never part of the production schema (verified with
-- `sqlite3 Day-4/QuotesApi/quotes.db ".schema Quotes"`). Task 1's report
-- worked around this by using Id as a recency signal.
--
-- Task 2 explicitly requires a "gap in days since the previous quote",
-- which needs real calendar dates, not just ordinal Ids. Rather than
-- inventing a CreatedAt column on the original Quotes table (which would
-- misrepresent the production schema), this script adds a SEPARATE,
-- clearly-labeled, Day-7-only table: QuotesTimeline.
--
-- QuotesTimeline is exercise data: it references the real Quotes rows by
-- QuoteId (and copies their real Author/Text) but attaches a synthetic
-- CreatedAt date to each one so that LAG()-based day-gap arithmetic has
-- something legitimate to operate on. The dates below are invented for
-- this exercise and are NOT derived from any real quote timestamps (none
-- exist). The original Quotes table/schema is not modified.
--
-- Deliberate design choices in the data, to make the window functions
-- genuinely demonstrate something (not just pass through 1:1):
--   * Some authors have only one quote (RowNumber/Rank = 1, no LAG value,
--     GapInDays = NULL - there is no "previous" quote).
--   * Albert Einstein has two quotes on the SAME synthetic date (Id 2 and
--     Id 3), so RANK() genuinely diverges from ROW_NUMBER() (RANK skips a
--     rank after a tie; ROW_NUMBER never ties) and GapInDays for that pair
--     is 0.
--   * The soft-deleted Mark Twain quote (Id 15, IsDeleted = 1) is excluded,
--     matching the same exclusion Task 1 applied.

CREATE TABLE "QuotesTimeline" (
    "Id"        INTEGER NOT NULL PRIMARY KEY,
    "QuoteId"   INTEGER NOT NULL REFERENCES "Quotes"("Id"),
    "Author"    TEXT NOT NULL,
    "Text"      TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL -- synthetic exercise-only date (YYYY-MM-DD), see header comment
);

INSERT INTO "QuotesTimeline" ("QuoteId", "Author", "Text", "CreatedAt") VALUES
    (1,  'Day5 Author',      'Day 5 sample quote after fix',                                                          '2024-01-01'),
    (2,  'Albert Einstein',  'Imagination is more important than knowledge.',                                        '2024-01-05'),
    (3,  'Albert Einstein',  'Life is like riding a bicycle. To keep your balance, you must keep moving.',            '2024-01-05'),
    (4,  'Albert Einstein',  'The important thing is not to stop questioning.',                                       '2024-02-20'),
    (5,  'Maya Angelou',     'There is no greater agony than bearing an untold story inside you.',                    '2024-01-10'),
    (6,  'Maya Angelou',     'I''ve learned that people will forget what you said, people will forget what you did, but people will never forget how you made them feel.', '2024-01-25'),
    (7,  'Mark Twain',       'The secret of getting ahead is getting started.',                                       '2024-01-12'),
    (8,  'Mark Twain',       'Kindness is the language which the deaf can hear and the blind can see.',                '2024-03-01'),
    (9,  'Ada Lovelace',     'That which is not exact is not knowledge.',                                             '2024-01-15'),
    (10, 'Marie Curie',      'Nothing in life is to be feared, it is only to be understood.',                        '2024-01-20'),
    (11, 'Marie Curie',      'I was taught that the way of progress was neither swift nor easy.',                    '2024-01-22'),
    (12, 'Nelson Mandela',   'It always seems impossible until it''s done.',                                          '2024-01-01'),
    (13, 'Nelson Mandela',   'Education is the most powerful weapon which you can use to change the world.',          '2024-01-10'),
    (14, 'Nelson Mandela',   'A good head and a good heart are always a formidable combination.',                     '2024-01-30');

-- Id 15 (the soft-deleted, retracted Mark Twain quote) is intentionally
-- NOT included above, matching Task 1's IsDeleted = 0 filtering.
