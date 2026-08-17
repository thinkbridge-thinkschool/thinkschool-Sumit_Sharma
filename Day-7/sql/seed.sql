-- Day-7 seed.sql
--
-- Row 1 below is the ACTUAL data currently tracked in Day-4/QuotesApi/quotes.db
-- (copied verbatim, not modified).
--
-- The real Quotes table in this repo has only ever held a single row at any
-- point in its git history (checked across all commits touching quotes.db) -
-- it was used for manual/deployment smoke testing, not as a real multi-author
-- quotes corpus. To exercise the join/CTE report meaningfully (multiple
-- authors, multiple quotes per author), the rows below labeled "seed data"
-- are added here for this exercise. They are real, correctly-attributed,
-- well-known public quotes (not fabricated attributions) - but they were not
-- part of the original app's persisted data, so they are called out
-- explicitly rather than passed off as pre-existing production data.
--
-- The Quotes table has no timestamp column in the real schema, so "most
-- recent quote" is defined by insertion order (the autoincrement Id) -
-- the same recency signal the real schema actually provides.

-- === Real row, copied from Day-4/QuotesApi/quotes.db ===
INSERT INTO "Quotes" ("Author", "Text", "IsDeleted") VALUES
    ('Day5 Author', 'Day 5 sample quote after fix', 0);

-- === Seed data added for this exercise (labeled, not original app data) ===
INSERT INTO "Quotes" ("Author", "Text", "IsDeleted") VALUES
    ('Albert Einstein', 'Imagination is more important than knowledge.', 0),
    ('Albert Einstein', 'Life is like riding a bicycle. To keep your balance, you must keep moving.', 0),
    ('Albert Einstein', 'The important thing is not to stop questioning.', 0),
    ('Maya Angelou', 'There is no greater agony than bearing an untold story inside you.', 0),
    ('Maya Angelou', 'I''ve learned that people will forget what you said, people will forget what you did, but people will never forget how you made them feel.', 0),
    ('Mark Twain', 'The secret of getting ahead is getting started.', 0),
    ('Mark Twain', 'Kindness is the language which the deaf can hear and the blind can see.', 0),
    ('Ada Lovelace', 'That which is not exact is not knowledge.', 0),
    ('Marie Curie', 'Nothing in life is to be feared, it is only to be understood.', 0),
    ('Marie Curie', 'I was taught that the way of progress was neither swift nor easy.', 0),
    ('Nelson Mandela', 'It always seems impossible until it''s done.', 0),
    ('Nelson Mandela', 'Education is the most powerful weapon which you can use to change the world.', 0),
    ('Nelson Mandela', 'A good head and a good heart are always a formidable combination.', 0);

-- A soft-deleted quote, to prove the report correctly excludes it via
-- WHERE IsDeleted = 0 (not just by coincidence of the data shape).
INSERT INTO "Quotes" ("Author", "Text", "IsDeleted") VALUES
    ('Mark Twain', 'This quote was retracted and should never appear in the report.', 1);
