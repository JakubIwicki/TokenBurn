using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TokenBurn.Processor.Persistence.Configurations;

public sealed class ModelAliasConfiguration : IEntityTypeConfiguration<ModelAlias>
{
    public void Configure(EntityTypeBuilder<ModelAlias> entity)
    {
        entity.ToTable(TableNames.ModelAliases);
        entity.HasKey(x => x.Alias);
        entity.Property(x => x.Alias).HasColumnName("alias").HasMaxLength(200);
        entity.Property(x => x.Service).HasColumnName("service").HasMaxLength(200).IsRequired();
        entity.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(200).IsRequired();
    }
}
