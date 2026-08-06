using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Persistence.Configurations;

public sealed class AgentMessageConfiguration : IEntityTypeConfiguration<AgentMessage>
{
    public void Configure(EntityTypeBuilder<AgentMessage> entity)
    {
        entity.ToTable(TableNames.AgentMessages);
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        entity.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        entity.Property(x => x.Role).HasColumnName("role").HasMaxLength(50).IsRequired();
        entity.Property(x => x.Content).HasColumnName("content").HasColumnType("text");
        entity.Property(x => x.ToolName).HasColumnName("tool_name").HasMaxLength(200);
        entity.Property(x => x.ModelSlug).HasColumnName("model_slug").HasMaxLength(200);
        entity.Property(x => x.InputTokens).HasColumnName("input_tokens").IsRequired();
        entity.Property(x => x.CacheReadTokens).HasColumnName("cache_read_tokens").IsRequired();
        entity.Property(x => x.CacheWriteTokens).HasColumnName("cache_write_tokens").IsRequired();
        entity.Property(x => x.OutputTokens).HasColumnName("output_tokens").IsRequired();
        entity.Property(x => x.CostUsd).HasColumnName("cost_usd").HasPrecision(20, 10);
        entity.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        entity.Property(x => x.Version).HasColumnName("version").IsRequired();
        entity.HasIndex(x => new { x.RunId, x.Sequence }).IsUnique();
        entity.HasIndex(x => new { x.RunId, x.OccurredAt });
        entity.HasOne<AgentRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}
