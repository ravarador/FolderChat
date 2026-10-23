using FolderChat.Core.Conversations;
using FolderChat.Core.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FolderChat.Infrastructure.Persistence;

public sealed class FolderChatDbContext(DbContextOptions<FolderChatDbContext> options) : DbContext(options)
{
    public DbSet<Provider> Providers => Set<Provider>();

    public DbSet<ProviderModel> ProviderModels => Set<ProviderModel>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ConversationMessage> Messages => Set<ConversationMessage>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Every key is a Guid the app creates itself. Telling EF they're never database-generated lets it
        // recognize a model added to a provider's Models list as new (an INSERT), not an existing row to update.
        modelBuilder.Entity<Provider>().Property(p => p.Id).ValueGeneratedNever();
        modelBuilder.Entity<ProviderModel>().Property(m => m.Id).ValueGeneratedNever();
        modelBuilder.Entity<Conversation>().Property(c => c.Id).ValueGeneratedNever();
        modelBuilder.Entity<ConversationMessage>().Property(m => m.Id).ValueGeneratedNever();
        modelBuilder.Entity<Attachment>().Property(a => a.Id).ValueGeneratedNever();

        modelBuilder.Entity<Provider>(provider =>
        {
            provider.Property(p => p.Name).HasMaxLength(100);
            provider.Property(p => p.BaseUrl).HasMaxLength(500);
            provider.HasMany(p => p.Models)
                .WithOne()
                .HasForeignKey(m => m.ProviderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProviderModel>(model =>
        {
            model.Property(m => m.ModelId).HasMaxLength(200);
            model.Property(m => m.DisplayName).HasMaxLength(200);
            model.HasIndex(m => new { m.ProviderId, m.ModelId }).IsUnique();
        });

        modelBuilder.Entity<Conversation>(conversation =>
        {
            conversation.Property(c => c.Title).HasMaxLength(200);
            conversation.Property(c => c.ModelId).HasMaxLength(200);
            conversation.HasIndex(c => c.UpdatedAt);
            conversation.HasMany(c => c.Messages)
                .WithOne()
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Messages keep a copy of the provider's name and ID rather than a foreign key,
        // so deleting a provider leaves old conversations intact.
        modelBuilder.Entity<ConversationMessage>(message =>
        {
            message.ToTable("Messages");
            message.Property(m => m.ProviderName).HasMaxLength(100);
            message.Property(m => m.ModelId).HasMaxLength(200);
            message.Property(m => m.Error).HasMaxLength(2000);
            message.HasIndex(m => new { m.ConversationId, m.Sequence }).IsUnique();

            // Optional, because a file has no message until it is sent. Deleting a conversation deletes its files.
            message.HasMany(m => m.Attachments)
                .WithOne()
                .HasForeignKey(a => a.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Attachment>(attachment =>
        {
            attachment.Property(a => a.FileName).HasMaxLength(255);
            attachment.Property(a => a.MediaType).HasMaxLength(100);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums as text keep the database file readable.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);

        // SQLite has no time zone type; every DateTime is stored as UTC and read back as UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
