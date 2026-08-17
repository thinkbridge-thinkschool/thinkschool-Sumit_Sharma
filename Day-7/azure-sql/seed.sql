-- Day-7 Azure SQL: seed.sql
--
-- T-SQL port of Day-7/sql/seed.sql, Day-7/sql/window-functions-seed.sql, and
-- Day-7/sql/set-operations-seed.sql. Same data, same Ids, as the local
-- SQLite Day-7 exercise, so the Azure SQL results are directly comparable
-- to the local SQLite results. See those files for the full rationale for
-- each row (real vs. exercise-only data).
--
-- Explicit Ids are preserved (matching the SQLite rows) because
-- QuotesTimeline / QuoteTags reference Quotes.Id by value.

SET IDENTITY_INSERT Quotes ON;

INSERT INTO Quotes (Id, Author, Text, IsDeleted) VALUES
    (1,  'Day5 Author',      'Day 5 sample quote after fix', 0),
    (2,  'Albert Einstein',  'Imagination is more important than knowledge.', 0),
    (3,  'Albert Einstein',  'Life is like riding a bicycle. To keep your balance, you must keep moving.', 0),
    (4,  'Albert Einstein',  'The important thing is not to stop questioning.', 0),
    (5,  'Maya Angelou',     'There is no greater agony than bearing an untold story inside you.', 0),
    (6,  'Maya Angelou',     'I''ve learned that people will forget what you said, people will forget what you did, but people will never forget how you made them feel.', 0),
    (7,  'Mark Twain',       'The secret of getting ahead is getting started.', 0),
    (8,  'Mark Twain',       'Kindness is the language which the deaf can hear and the blind can see.', 0),
    (9,  'Ada Lovelace',     'That which is not exact is not knowledge.', 0),
    (10, 'Marie Curie',      'Nothing in life is to be feared, it is only to be understood.', 0),
    (11, 'Marie Curie',      'I was taught that the way of progress was neither swift nor easy.', 0),
    (12, 'Nelson Mandela',   'It always seems impossible until it''s done.', 0),
    (13, 'Nelson Mandela',   'Education is the most powerful weapon which you can use to change the world.', 0),
    (14, 'Nelson Mandela',   'A good head and a good heart are always a formidable combination.', 0),
    (15, 'Mark Twain',       'This quote was retracted and should never appear in the report.', 1);

SET IDENTITY_INSERT Quotes OFF;
GO

-- QuotesTimeline: synthetic CreatedAt dates for Task 2 (window functions).
-- Id 15 (soft-deleted) is intentionally excluded, matching Task 1 filtering.
INSERT INTO QuotesTimeline (Id, QuoteId, Author, Text, CreatedAt) VALUES
    (1,  1,  'Day5 Author',      'Day 5 sample quote after fix',                                                          '2024-01-01'),
    (2,  2,  'Albert Einstein',  'Imagination is more important than knowledge.',                                        '2024-01-05'),
    (3,  3,  'Albert Einstein',  'Life is like riding a bicycle. To keep your balance, you must keep moving.',           '2024-01-05'),
    (4,  4,  'Albert Einstein',  'The important thing is not to stop questioning.',                                      '2024-02-20'),
    (5,  5,  'Maya Angelou',     'There is no greater agony than bearing an untold story inside you.',                   '2024-01-10'),
    (6,  6,  'Maya Angelou',     'I''ve learned that people will forget what you said, people will forget what you did, but people will never forget how you made them feel.', '2024-01-25'),
    (7,  7,  'Mark Twain',       'The secret of getting ahead is getting started.',                                      '2024-01-12'),
    (8,  8,  'Mark Twain',       'Kindness is the language which the deaf can hear and the blind can see.',              '2024-03-01'),
    (9,  9,  'Ada Lovelace',     'That which is not exact is not knowledge.',                                            '2024-01-15'),
    (10, 10, 'Marie Curie',      'Nothing in life is to be feared, it is only to be understood.',                       '2024-01-20'),
    (11, 11, 'Marie Curie',      'I was taught that the way of progress was neither swift nor easy.',                   '2024-01-22'),
    (12, 12, 'Nelson Mandela',   'It always seems impossible until it''s done.',                                        '2024-01-01'),
    (13, 13, 'Nelson Mandela',   'Education is the most powerful weapon which you can use to change the world.',        '2024-01-10'),
    (14, 14, 'Nelson Mandela',   'A good head and a good heart are always a formidable combination.',                   '2024-01-30');
GO

-- Tags / QuoteTags / AuthorCategories for Task 3 (set operations).
INSERT INTO Tags (Id, Name) VALUES
    (1, 'knowledge'),
    (2, 'perseverance'),
    (3, 'kindness'),
    (4, 'science'),
    (5, 'courage'),
    (6, 'education'),
    (7, 'inspiration');

-- QuoteId 1 (Day5 Author) and QuoteId 9 (Ada Lovelace) are intentionally
-- left untagged, so Task 3 Q1 (EXCEPT) has a genuine non-empty answer.
INSERT INTO QuoteTags (QuoteId, TagId) VALUES
    (2, 1),
    (3, 2),
    (4, 2),
    (5, 5),
    (6, 7),
    (7, 2),
    (8, 3),
    (10, 4),
    (11, 2),
    (12, 5),
    (13, 6),
    (14, 5);

-- Nelson Mandela and Ada Lovelace are deliberately in BOTH lists, so Task 3
-- Q2 (INTERSECT) has a genuine non-empty answer.
INSERT INTO AuthorCategories (Author, Category) VALUES
    ('Albert Einstein', 'classic'),
    ('Mark Twain',       'classic'),
    ('Marie Curie',      'classic'),
    ('Nelson Mandela',   'classic'),
    ('Ada Lovelace',     'classic'),
    ('Nelson Mandela',   'modern'),
    ('Maya Angelou',     'modern'),
    ('Ada Lovelace',     'modern'),
    ('Day5 Author',      'modern');
GO
