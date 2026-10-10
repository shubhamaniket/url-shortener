using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UrlShortener.Domain.Links;

namespace UrlShortener.Infrastructure.Persistence;

internal sealed class ShortLinkConfiguration : IEntityTypeConfiguration<ShortLink>
{
    public void Configure(EntityTypeBuilder<ShortLink> builder)
    {
        builder.ToTable("ShortLinks");

        builder.HasKey(link => link.Id);

        builder.Property(link => link.Code)
            .IsRequired()
            .HasMaxLength(ShortLink.MaxCodeLength);

        builder.Property(link => link.NormalizedCode)
            .IsRequired()
            .HasMaxLength(ShortLink.MaxCodeLength);

        // Enforces every uniqueness rule (alias vs alias, alias vs generated, generated vs alias)
        // and serves the redirect lookup (research R3).
        builder.HasIndex(link => link.NormalizedCode)
            .IsUnique()
            .HasDatabaseName("IX_ShortLinks_NormalizedCode");

        builder.Property(link => link.IsCustomAlias)
            .IsRequired();

        builder.Property(link => link.OriginalUrl)
            .IsRequired()
            .HasMaxLength(ShortLink.MaxUrlLength);

        // SQLite stores DateTime as text and reads it back as Unspecified; mark it as UTC.
        builder.Property(link => link.CreatedAtUtc)
            .IsRequired()
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        builder.Property(link => link.ClickCount)
            .IsRequired();
    }
}