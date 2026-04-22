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
    }
}
