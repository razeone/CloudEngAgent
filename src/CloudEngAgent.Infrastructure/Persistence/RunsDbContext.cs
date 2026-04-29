using CloudEngAgent.Domain.Runs;
using CloudEngAgent.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CloudEngAgent.Infrastructure.Persistence;

public sealed class RunsDbContext(DbContextOptions<RunsDbContext> options) : DbContext(options)
{
    public DbSet<RunEntity> Runs => Set<RunEntity>();
    public DbSet<RunEventEntity> RunEvents => Set<RunEventEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<RunWidgetStateEntity> RunWidgetStates => Set<RunWidgetStateEntity>();
    public DbSet<InputRequestEntity> InputRequests => Set<InputRequestEntity>();
    public DbSet<ArtifactEntity> Artifacts => Set<ArtifactEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var runStatusConverter = new ValueConverter<RunStatus, byte>(
            v => (byte)v,
            v => (RunStatus)v);

        modelBuilder.Entity<RunEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.WorkflowId).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Status)
                .HasConversion(runStatusConverter)
                .HasColumnType("tinyint");
            entity.Property(e => e.StartedAt).HasColumnType("datetimeoffset");
            entity.Property(e => e.EndedAt).HasColumnType("datetimeoffset");
            entity.Property(e => e.InputSummary).HasColumnType("nvarchar(max)");
        });

        var runEventTypeConverter = new ValueConverter<RunEventType, byte>(
            v => (byte)v,
            v => (RunEventType)v);

        modelBuilder.Entity<RunEventEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Type)
                .HasConversion(runEventTypeConverter)
                .HasColumnType("tinyint");
            entity.Property(e => e.PayloadJson)
                .IsRequired()
                .HasColumnType("nvarchar(max)");
            entity.HasOne(e => e.Run)
                .WithMany(r => r.Events)
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.RunId, e.SequenceNo }).IsUnique();
        });

        var messageRoleConverter = new ValueConverter<MessageRole, byte>(
            v => (byte)v,
            v => (MessageRole)v);

        modelBuilder.Entity<MessageEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Role)
                .HasConversion(messageRoleConverter)
                .HasColumnType("tinyint");
            entity.Property(e => e.Content)
                .IsRequired()
                .HasColumnType("nvarchar(max)");
            entity.HasOne(e => e.Run)
                .WithMany(r => r.Messages)
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RunWidgetStateEntity>(entity =>
        {
            entity.ToTable("RunWidgetStates");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.WidgetKey).IsRequired().HasMaxLength(450);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Status).HasColumnType("int");
            entity.Property(e => e.Revision).HasColumnType("int");
            entity.Property(e => e.Placement_Surface).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Placement_StepId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Placement_AgentId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Placement_ParentMessageId).HasMaxLength(128);
            entity.Property(e => e.PropsJson).IsRequired().HasColumnType("nvarchar(max)");
            entity.Property(e => e.ArtifactsJson).IsRequired().HasColumnType("nvarchar(max)");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetimeoffset");
            entity.HasOne<RunEntity>()
                .WithMany()
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.RunId, e.WidgetKey }).IsUnique();
            entity.HasIndex(e => new { e.RunId, e.UpdatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_RunWidgetStates_RunId_UpdatedAt");
        });

        modelBuilder.Entity<InputRequestEntity>(entity =>
        {
            entity.ToTable("InputRequests");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.StepId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.AgentId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.SchemaRef).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Status).HasColumnType("int");
            entity.Property(e => e.CreatedAt).HasColumnType("datetimeoffset");
            entity.Property(e => e.ExpiresAt).HasColumnType("datetimeoffset");
            entity.Property(e => e.PayloadJson).HasColumnType("nvarchar(max)");
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.HasOne<RunEntity>()
                .WithMany()
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.RunId, e.Status });
        });

        modelBuilder.Entity<ArtifactEntity>(entity =>
        {
            entity.ToTable("Artifacts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Kind).HasColumnType("int");
            entity.Property(e => e.Filename).IsRequired().HasMaxLength(260);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(128);
            entity.Property(e => e.SizeBytes).HasColumnType("bigint");
            entity.Property(e => e.ContentSha256)
                .IsRequired()
                .HasColumnType("char(64)")
                .IsFixedLength();
            entity.Property(e => e.CreatedAt).HasColumnType("datetimeoffset");
            entity.HasOne<RunEntity>()
                .WithMany()
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.RunId, e.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_Artifacts_RunId_CreatedAt");
            entity.HasIndex(e => e.ContentSha256)
                .HasDatabaseName("IX_Artifacts_ContentSha256");
        });
    }
}
