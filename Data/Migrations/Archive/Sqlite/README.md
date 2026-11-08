# Retired SQLite migrations

These files preserve RentRoad's original SQLite migration chain and its final model snapshot. They are historical source only and must not be applied to SQL Server.

Each original C# filename has `.txt` appended. This keeps the source available in Git while excluding it from C# compilation and EF Core migration/snapshot discovery without changing the application project configuration.

The archive contains `CreateIdentitySchema`, `AddRentRoadSchema`, their designers, and the SQLite `ApplicationDbContextModelSnapshot`. The active chain starts with `InitialRentRoadSqlServer` in the parent migrations directory.

Commit the archive together with the SQL Server replacement so the previously uncommitted SQLite schema work is retained. The original Identity migration also remains available in earlier Git history.
