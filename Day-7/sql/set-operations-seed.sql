-- Day-7 Task 3: set-operations-seed.sql
--
-- The real Quotes table (schema.sql) has no notion of tags, and no notion
-- of a "classic" vs "modern" author grouping - neither concept exists
-- anywhere in the production schema. To exercise EXCEPT / INTERSECT /
-- UNION meaningfully, this script adds three SEPARATE, clearly-labeled,
-- Day-7-only tables. The original Quotes table/schema is not modified.
--
--   Tags             - a small vocabulary of quote tags.
--   QuoteTags        - many-to-many join of real Quotes.Id to Tags.Id.
--                      Two authors (Ada Lovelace and Day5 Author) are left
--                      with no tagged quotes at all, on purpose, so that
--                      "authors who have quotes but no tags" is a genuine,
--                      non-trivial result rather than an empty or
--                      everyone-matches set.
--   AuthorCategories - an arbitrary, exercise-only classification of each
--                      Author as 'classic' and/or 'modern' (an author can
--                      be in one, both, or neither list). This is NOT a
--                      factual claim about any author's era - it exists
--                      only so Task 3's INTERSECT question has two real,
--                      overlapping sets to work with. Nelson Mandela and
--                      Ada Lovelace are deliberately placed in both lists.
--
-- As with QuotesTimeline (Task 2), all of this is invented exercise data,
-- clearly labeled as such, layered on top of the real Quotes rows via
-- QuoteId foreign keys - not a rewrite of the original schema.

CREATE TABLE "Tags" (
    "Id"   INTEGER NOT NULL PRIMARY KEY,
    "Name" TEXT NOT NULL UNIQUE
);

CREATE TABLE "QuoteTags" (
    "QuoteId" INTEGER NOT NULL REFERENCES "Quotes"("Id"),
    "TagId"   INTEGER NOT NULL REFERENCES "Tags"("Id"),
    PRIMARY KEY ("QuoteId", "TagId")
);

CREATE TABLE "AuthorCategories" (
    "Author"   TEXT NOT NULL,
    "Category" TEXT NOT NULL CHECK ("Category" IN ('classic', 'modern')),
    PRIMARY KEY ("Author", "Category")
);

INSERT INTO "Tags" ("Id", "Name") VALUES
    (1, 'knowledge'),
    (2, 'perseverance'),
    (3, 'kindness'),
    (4, 'science'),
    (5, 'courage'),
    (6, 'education'),
    (7, 'inspiration');

-- QuoteId 1 (Day5 Author) and QuoteId 9 (Ada Lovelace) are intentionally
-- left untagged.
INSERT INTO "QuoteTags" ("QuoteId", "TagId") VALUES
    (2, 1),  -- Einstein: "Imagination is more important..." -> knowledge
    (3, 2),  -- Einstein: "Life is like riding a bicycle..."  -> perseverance
    (4, 2),  -- Einstein: "The important thing is not to..."  -> perseverance
    (5, 5),  -- Angelou:  "There is no greater agony..."      -> courage
    (6, 7),  -- Angelou:  "I've learned that people..."       -> inspiration
    (7, 2),  -- Twain:    "The secret of getting ahead..."    -> perseverance
    (8, 3),  -- Twain:    "Kindness is the language..."       -> kindness
    (10, 4), -- Curie:    "Nothing in life is to be feared..."-> science
    (11, 2), -- Curie:    "I was taught that the way..."      -> perseverance
    (12, 5), -- Mandela:  "It always seems impossible..."     -> courage
    (13, 6), -- Mandela:  "Education is the most powerful..." -> education
    (14, 5); -- Mandela:  "A good head and a good heart..."   -> courage

-- Arbitrary, exercise-only classic/modern classification (see header).
-- Nelson Mandela and Ada Lovelace are in BOTH lists on purpose, to give
-- the INTERSECT query a real, non-empty answer.
INSERT INTO "AuthorCategories" ("Author", "Category") VALUES
    ('Albert Einstein', 'classic'),
    ('Mark Twain',       'classic'),
    ('Marie Curie',      'classic'),
    ('Nelson Mandela',   'classic'),
    ('Ada Lovelace',     'classic'),
    ('Nelson Mandela',   'modern'),
    ('Maya Angelou',     'modern'),
    ('Ada Lovelace',     'modern'),
    ('Day5 Author',      'modern');
