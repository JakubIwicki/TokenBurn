using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Persistence.Configurations;

public sealed class ImportCommandConfiguration : IEntityTypeConfiguration<ImportCommand>
{
    public void Configure(EntityTypeBuilder<ImportCommand> entity)
    {
        entity.ToTable(TableNames.ImportCommands, table =>
        {
            table.HasCheckConstraint("ck_import_commands_status", "status IN ('Queued','Running','Completed','Failed')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.Type).HasColumnName("type").HasMaxLength(200).IsRequired();
        entity.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        entity.Property(x => x.Attempts).HasColumnName("attempts");
        entity.Property(x => x.HandlingStartedAt).HasColumnName("handling_started_at");
        entity.Property(x => x.CooldownUntil).HasColumnName("cooldown_until");
        entity.Property(x => x.LastError).HasColumnName("last_error");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
        // The claim query filters on (status, cooldown_until); jsonb in the btree unique index
        // below is fine for exact-equality dedup of a queued/running command (the 409 guard).
        entity.HasIndex(x => new { x.Status, x.CooldownUntil });
        entity.HasIndex(x => new { x.Type, x.Payload }).IsUnique().HasFilter("status IN ('Queued','Running')");
    }
}
