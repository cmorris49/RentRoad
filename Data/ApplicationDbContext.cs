using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RentRoad.Models;
using RentRoad.Models.Enums;

namespace RentRoad.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Listing> Listings => Set<Listing>();

    public DbSet<ListingImage> ListingImages => Set<ListingImage>();

    public DbSet<AvailabilityWindow> AvailabilityWindows => Set<AvailabilityWindow>();

    public DbSet<AvailabilityBlock> AvailabilityBlocks => Set<AvailabilityBlock>();

    public DbSet<RentalRequest> RentalRequests => Set<RentalRequest>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<UserActivity> UserActivities => Set<UserActivity>();

    /// <summary>
    /// Configures the application entities, relationships, indexes, and database defaults.
    /// </summary>
    /// <param name="modelBuilder">The builder used to configure the entity model.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureUser(modelBuilder);
        ConfigureListing(modelBuilder);
        ConfigureListingImage(modelBuilder);
        ConfigureAvailabilityWindow(modelBuilder);
        ConfigureAvailabilityBlock(modelBuilder);
        ConfigureRentalRequest(modelBuilder);
        ConfigureConversation(modelBuilder);
        ConfigureMessage(modelBuilder);
        ConfigureReview(modelBuilder);
        ConfigureUserActivity(modelBuilder);
        ConfigureUtcTimestamps(modelBuilder);
    }

    /// <summary>
    /// Configures the application user entity.
    /// </summary>
    /// <param name="modelBuilder">The builder used to configure the entity model.</param>
    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.DisplayName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(user => user.Location)
                .HasMaxLength(150);
        });
    }

    /// <summary>
    /// Configures listing price and enum constraints, indexes, and the owner relationship.
    /// </summary>
    /// <param name="modelBuilder">The builder used to configure the entity model.</param>
    private static void ConfigureListing(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Listing>(entity =>
        {
            entity.HasKey(listing => listing.ListingId);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Listings_DailyPriceCents", "[DailyPriceCents] >= 0");
                table.HasCheckConstraint("CK_Listings_PublishedPrice",
                    $"[Status] <> {(int)ListingStatus.Published} OR ([DailyPriceCents] IS NOT NULL AND [DailyPriceCents] > 0)");
                table.HasCheckConstraint("CK_Listings_Status", CreateEnumCheckConstraint<ListingStatus>(nameof(Listing.Status)));
                table.HasCheckConstraint("CK_Listings_Category", CreateEnumCheckConstraint<ListingCategory>(nameof(Listing.Category)));
                table.HasCheckConstraint("CK_Listings_Condition", CreateEnumCheckConstraint<ItemCondition>(nameof(Listing.Condition)));
            });
            entity.Property(listing => listing.Status)
                .HasDefaultValue(ListingStatus.Draft)
                .IsRequired();
            entity.Property(listing => listing.CreatedAtUtc).IsRequired();
            entity.Property(listing => listing.UpdatedAtUtc).IsRequired();
            entity.HasIndex(listing => new { listing.Status, listing.Category });
            entity.HasIndex(listing => new { listing.OwnerId, listing.Status });
            entity.HasIndex(listing => listing.PickupArea);
            entity.HasOne(listing => listing.Owner)
                .WithMany(user => user.Listings)
                .HasForeignKey(listing => listing.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>
    /// Configures image size constraints, unique blob identities, and at most one primary image per listing.
    /// </summary>
    /// <remarks>
    /// A persisted SHA-256 blob-name key keeps the unique index within SQL Server's key-size limit
    /// while retaining full-length, case-sensitive Unicode blob names.
    /// </remarks>
    /// <param name="modelBuilder">The builder used to configure the entity model.</param>
    private static void ConfigureListingImage(ModelBuilder modelBuilder)
    {
        const string BLOB_NAME_HASH = "BlobNameHash";

        modelBuilder.Entity<ListingImage>(entity =>
        {
            entity.HasKey(image => image.ListingImageId);
            entity.ToTable(table => table.HasCheckConstraint("CK_ListingImages_SizeBytes", "[SizeBytes] >= 0"));
            entity.Property(image => image.ContainerName)
                .HasMaxLength(100)
                .UseCollation("Latin1_General_100_BIN2")
                .IsRequired();
            entity.Property(image => image.BlobName)
                .HasMaxLength(1024)
                .UseCollation("Latin1_General_100_BIN2")
                .IsRequired();
            entity.Property<byte[]>(BLOB_NAME_HASH)
                .HasColumnType("binary(32)")
                .HasComputedColumnSql("CONVERT(binary(32), HASHBYTES('SHA2_256', [BlobName]))", stored: true)
                .IsRequired();
            entity.Property(image => image.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(image => image.IsPrimary).HasDefaultValue(false).IsRequired();
            entity.Property(image => image.DisplayOrder).HasDefaultValue(0).IsRequired();
            entity.Property(image => image.CreatedAtUtc).IsRequired();
            entity.HasIndex(nameof(ListingImage.ContainerName), BLOB_NAME_HASH).IsUnique();
            entity.HasIndex(image => new { image.ListingId, image.IsPrimary });
            entity.HasIndex(image => image.ListingId)
                .IsUnique()
                .HasFilter("[IsPrimary] = 1");
            entity.HasOne(image => image.Listing)
                .WithMany(listing => listing.Images)
                .HasForeignKey(image => image.ListingId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>
    /// Configures availability window date constraints, indexes, and the listing relationship.
    /// </summary>
    /// <param name="modelBuilder">The builder used to configure the entity model.</param>
    private static void ConfigureAvailabilityWindow(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AvailabilityWindow>(entity =>
        {
            entity.HasKey(window => window.AvailabilityWindowId);
            entity.ToTable(table => table.HasCheckConstraint("CK_AvailabilityWindows_DateRange", "[StartDate] < [EndDate]"));
            entity.Property(window => window.StartDate).IsRequired();
            entity.Property(window => window.EndDate).IsRequired();
            entity.HasIndex(window => new { window.ListingId, window.StartDate, window.EndDate });
            entity.HasOne(window => window.Listing)
                .WithMany(listing => listing.AvailabilityWindows)
                .HasForeignKey(window => window.ListingId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>
    /// Configures availability block date constraints, indexes, and the listing relationship.
    /// </summary>
    /// <param name="modelBuilder">The builder used to configure the entity model.</param>
    private static void ConfigureAvailabilityBlock(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AvailabilityBlock>(entity =>
        {
            entity.HasKey(block => block.AvailabilityBlockId);
            entity.ToTable(table => table.HasCheckConstraint("CK_AvailabilityBlocks_DateRange", "[StartDate] < [EndDate]"));
            entity.Property(block => block.StartDate).IsRequired();
            entity.Property(block => block.EndDate).IsRequired();
            entity.HasIndex(block => new { block.ListingId, block.StartDate, block.EndDate });
            entity.HasOne(block => block.Listing)
                .WithMany(listing => listing.AvailabilityBlocks)
                .HasForeignKey(block => block.ListingId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>
    /// Configures rental date, price, and enum constraints, indexes, and historical relationships.
    /// </summary>
    /// <param name="modelBuilder">The builder used to configure the entity model.</param>
    private static void ConfigureRentalRequest(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RentalRequest>(entity =>
        {
            entity.HasKey(request => request.RentalRequestId);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_RentalRequests_DateRange", "[StartDate] < [EndDate]");
                table.HasCheckConstraint("CK_RentalRequests_DailyPriceAtRequestCents", "[DailyPriceAtRequestCents] >= 0");
                table.HasCheckConstraint("CK_RentalRequests_TotalPriceCents", "[TotalPriceCents] >= 0");
                table.HasCheckConstraint("CK_RentalRequests_Status", CreateEnumCheckConstraint<RentalStatus>(nameof(RentalRequest.Status)));
            });
            entity.Property(request => request.Status)
                .HasDefaultValue(RentalStatus.Pending)
                .IsRequired();
            entity.Property(request => request.CreatedAtUtc).IsRequired();
            entity.Property(request => request.DailyPriceAtRequestCents).IsRequired();
            entity.Property(request => request.TotalPriceCents).IsRequired();
            entity.HasIndex(request => new { request.ListingId, request.Status, request.StartDate, request.EndDate });
            entity.HasIndex(request => new { request.RenterId, request.Status });
            entity.HasOne(request => request.Listing)
                .WithMany(listing => listing.RentalRequests)
                .HasForeignKey(request => request.ListingId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(request => request.Renter)
                .WithMany(user => user.RentalRequests)
                .HasForeignKey(request => request.RenterId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>
    /// Configures conversations and their participant and uniqueness constraints.
    /// </summary>
    /// <param name="modelBuilder">The builder used to configure the entity model.</param>
    private static void ConfigureConversation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(conversation => conversation.ConversationId);
            entity.Property(conversation => conversation.CreatedAtUtc).IsRequired();
            entity.HasIndex(conversation => new { conversation.ListingId, conversation.RenterId }).IsUnique();
            entity.HasOne(conversation => conversation.Listing)
                .WithMany(listing => listing.Conversations)
                .HasForeignKey(conversation => conversation.ListingId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(conversation => conversation.Renter)
                .WithMany(user => user.RentedConversations)
                .HasForeignKey(conversation => conversation.RenterId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>
    /// Configures messages, sender relationships, scoped retry protection, and history indexes.
    /// </summary>
    /// <param name="modelBuilder">The builder used to configure the entity model.</param>
    private static void ConfigureMessage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(message => message.MessageId);
            entity.Property(message => message.Content).HasMaxLength(2000).IsRequired();
            entity.Property(message => message.SentAtUtc).IsRequired();
            entity.HasIndex(message => new { message.ConversationId, message.SentAtUtc });
            entity.HasIndex(message => new { message.ConversationId, message.MessageId });
            entity.HasIndex(message => new { message.ConversationId, message.SenderId, message.ClientMessageId })
                .IsUnique()
                .HasFilter("[ClientMessageId] IS NOT NULL");
            entity.HasIndex(message => message.SenderId);
            entity.HasOne(message => message.Conversation)
                .WithMany(conversation => conversation.Messages)
                .HasForeignKey(message => message.ConversationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(message => message.Sender)
                .WithMany(user => user.Messages)
                .HasForeignKey(message => message.SenderId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>
    /// Configures the rating constraint and the unique review relationship.
    /// </summary>
    /// <param name="modelBuilder">The builder used to configure the entity model.</param>
    private static void ConfigureReview(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(review => review.ReviewId);
            entity.ToTable(table => table.HasCheckConstraint("CK_Reviews_Rating", "[Rating] BETWEEN 1 AND 5"));
            entity.Property(review => review.Rating).IsRequired();
            entity.Property(review => review.CreatedAtUtc).IsRequired();
            entity.HasIndex(review => review.RentalRequestId).IsUnique();
            entity.HasOne(review => review.RentalRequest)
                .WithOne(request => request.Review)
                .HasForeignKey<Review>(review => review.RentalRequestId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>
    /// Configures optional historical categories, enum constraints, relationships, and activity query indexes.
    /// </summary>
    /// <param name="modelBuilder">The builder used to configure the entity model.</param>
    private static void ConfigureUserActivity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserActivity>(entity =>
        {
            entity.HasKey(activity => activity.UserActivityId);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_UserActivities_Category", CreateEnumCheckConstraint<ListingCategory>(nameof(UserActivity.Category)));
                table.HasCheckConstraint("CK_UserActivities_ActivityType", CreateEnumCheckConstraint<UserActivityType>(nameof(UserActivity.ActivityType)));
            });
            entity.Property(activity => activity.ActivityType).IsRequired();
            entity.Property(activity => activity.CreatedAtUtc).IsRequired();
            entity.HasIndex(activity => new { activity.UserId, activity.CreatedAtUtc });
            entity.HasIndex(activity => new { activity.ListingId, activity.ActivityType });
            entity.HasOne(activity => activity.User)
                .WithMany(user => user.Activities)
                .HasForeignKey(activity => activity.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(activity => activity.Listing)
                .WithMany(listing => listing.Activities)
                .HasForeignKey(activity => activity.ListingId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>
    /// Configures timestamp conversion for SQL Server datetime2 columns without losing UTC semantics.
    /// </summary>
    /// <remarks>
    /// Local inputs are converted to UTC; unspecified inputs are treated as UTC. Read values have UTC kind.
    /// Nullable timestamps retain null values through EF Core's value conversion handling.
    /// </remarks>
    /// <param name="modelBuilder">The builder used to configure timestamp properties.</param>
    private static void ConfigureUtcTimestamps(ModelBuilder modelBuilder)
    {
        var converter = new ValueConverter<DateTime, DateTime>(
            value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        foreach (var property in modelBuilder.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property => property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?)))
        {
            property.SetValueConverter(converter);
        }
    }

    /// <summary>
    /// Builds a SQL Server check expression allowing defined enum values and nullable columns.
    /// </summary>
    /// <typeparam name="TEnum">The persisted enum type.</typeparam>
    /// <param name="propertyName">The mapped column name.</param>
    /// <returns>A SQL expression that rejects undefined enum values.</returns>
    private static string CreateEnumCheckConstraint<TEnum>(string propertyName) where TEnum : struct, Enum
    {
        var values = string.Join(", ", Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value)));
        return $"[{propertyName}] IN ({values})";
    }
}
