# Why a Rich Domain Model?

The old Quote model was mainly a container for data. Its properties could be changed directly, and some validation was performed in the API endpoint. This meant that another part of the application could potentially create or modify a Quote without following the same business rules.

The new rich Quote model keeps its important rules inside the Quote itself. Quotes are created through `Quote.Create(author, text)`, which validates the author and text before a Quote can be created. The Author must be between 1 and 200 characters, while the Text must be between 1 and 1000 characters.

The Text property can no longer be changed directly after creation because its setter is private. Deletion is also controlled by the domain through `MarkDeleted()`, which performs a soft delete instead of removing the database record.

A specific bug this prevents is another part of the application accidentally creating a Quote with an empty author or extremely long text and saving it directly to the database. With the rich model, creation always goes through the validation rules.

Overall, the rich model keeps business rules close to the data they protect, making the application easier to maintain and reducing the chance that different parts of the system behave inconsistently.
