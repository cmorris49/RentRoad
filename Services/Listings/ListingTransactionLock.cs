using Microsoft.EntityFrameworkCore;
using RentRoad.Data;

namespace RentRoad.Services.Listings;

/// <summary>Coordinates SQL Server rental decisions and availability mutations through a single listing's primary-key row.</summary>
internal static class ListingTransactionLock
{
    /// <summary>Acquires an update lock until transaction completion; SQLite tests rely on their existing serializable writer transaction.</summary>
    internal static async Task AcquireAsync(ApplicationDbContext context, int listingId, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A listing lock requires an active transaction.");
        }
        if (context.Database.IsSqlServer())
        {
            await Query(context, listingId).ToListAsync(cancellationToken);
        }
    }

    /// <summary>Builds the parameterized primary-key lock query without interpolating identifiers or client text into SQL.</summary>
    internal static IQueryable<int> Query(ApplicationDbContext context, int listingId) => context.Database.SqlQuery<int>(
        $"SELECT [ListingId] AS [Value] FROM [Listings] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [ListingId] = {listingId}");
}
