using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Collection> Collections => Set<Collection>();

    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public DbSet<DigestNotification> DigestNotifications => Set<DigestNotification>();

    public DbSet<DeadLetteredMessage> DeadLetteredMessages => Set<DeadLetteredMessage>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProcessedMessage>(entity =>
        {
            entity.HasIndex(processed => new { processed.Subscription, processed.MessageId })
                .IsUnique();
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(collection => collection.Id);

            entity.Property(collection => collection.Name)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(collection => collection.OwnerId)
                .IsRequired();

            entity.OwnsMany(
                collection => collection.Items,
                item =>
                {
                    item.ToTable("CollectionItems");

                    item.WithOwner()
                        .HasForeignKey("CollectionId");

                    item.HasKey(
                        "CollectionId",
                        nameof(CollectionItem.QuoteId));

                    item.Property(
                        collectionItem => collectionItem.QuoteId)
                        .IsRequired()
                        .ValueGeneratedNever();

                    item.Property(
                        collectionItem => collectionItem.AddedAt)
                        .IsRequired();
                });
        });
    }
}
