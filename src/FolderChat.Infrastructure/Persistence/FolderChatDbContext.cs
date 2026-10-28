using FolderChat.Core.Api;
using FolderChat.Core.Conversations;
using FolderChat.Core.Knowledge;
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

    public DbSet<KnowledgeFolder> KnowledgeFolders => Set<KnowledgeFolder>();

    public DbSet<KnowledgeFile> KnowledgeFiles => Set<KnowledgeFile>();

    public DbSet<Passage> Passages => Set<Passage>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Every key is a Guid the app creates itself. Telling EF they're never database-generated lets it
        // recognize a model added to a provider's Models list as new (an INSERT), not an existing row to update.
        modelBuilder.Entity<Provider>().Property(p => p.Id).ValueGeneratedNever();
        modelBuilder.Entity<ProviderModel>().Property(m => m.Id).ValueGeneratedNever();
        modelBuilder.Entity<Conversation>().Property(c => c.Id).ValueGeneratedNever();
        modelBuilder.Entity<ConversationMessage>().Property(m => m.Id).ValueGeneratedNever();
        modelBuilder.Entity<Attachment>().Property(a => a.Id).ValueGeneratedNever();
        modelBuilder.Entity<KnowledgeFolder>().Property(f => f.Id).ValueGeneratedNever();
        modelBuilder.Entity<KnowledgeFile>().Property(f => f.Id).ValueGeneratedNever();
        modelBuilder.Entity<ApiKey>().Property(k => k.Id).ValueGeneratedNever();

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
            conversation.Property(c => c.ExternalUser).HasMaxLength(200);

            // Existing conversations were all started in the app.
            conversation.Property(c => c.Source).HasDefaultValue(ConversationSource.App).HasSentinel(ConversationSource.App);
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

            // Stored as JSON in the message row: they're always read with the message, and never queried.
            message.OwnsMany(m => m.Sources, sources =>
            {
                sources.ToJson();
                sources.Property(s => s.Path).HasMaxLength(1024);
            });
        });

        modelBuilder.Entity<Attachment>(attachment =>
        {
            attachment.Property(a => a.FileName).HasMaxLength(255);
            attachment.Property(a => a.MediaType).HasMaxLength(100);
        });

        modelBuilder.Entity<KnowledgeFolder>(folder =>
        {
            folder.Property(f => f.Name).HasMaxLength(100);
            folder.Property(f => f.Path).HasMaxLength(1024);
            folder.Property(f => f.ScanError).HasMaxLength(2000);
            folder.Property(f => f.SummaryModelId).HasMaxLength(200);
            folder.HasMany(f => f.Files)
                .WithOne()
                .HasForeignKey(f => f.FolderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KnowledgeFile>(file =>
        {
            file.Property(f => f.RelativePath).HasMaxLength(1024);
            file.Property(f => f.Note).HasMaxLength(500);
            file.Property(f => f.MediaType).HasMaxLength(100);
            file.HasIndex(f => new { f.FolderId, f.RelativePath }).IsUnique();
        });

        modelBuilder.Entity<ApiKey>(key =>
        {
            key.Property(k => k.Name).HasMaxLength(100);
            key.Property(k => k.Prefix).HasMaxLength(20);
            key.Property(k => k.Hash).HasMaxLength(64);
            key.HasIndex(k => k.Hash).IsUnique();
        });

        // Passages are also in a full-text index, a SQLite FTS5 table that EF doesn't model. The AddKnowledgeFolders
        // migration creates it, with triggers that keep it in step with this table. A future migration that rebuilds
        // this table must create those triggers again.
        modelBuilder.Entity<Passage>(passage =>
        {
            passage.HasOne<KnowledgeFile>()
                .WithMany()
                .HasForeignKey(p => p.FileId)
                .OnDelete(DeleteBehavior.Cascade);
            passage.HasIndex(p => new { p.FileId, p.Ordinal });
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
