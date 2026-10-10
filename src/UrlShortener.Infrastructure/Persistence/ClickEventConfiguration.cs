using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UrlShortener.Domain.Links;

namespace UrlShortener.Infrastructure.Persistence;

internal sealed class ClickEventConfiguration : IEntityTypeConfiguration<ClickEvent>
{
    public void Configure(EntityTypeBuilder<ClickEvent> builder)
    {
        builder.ToTable("ClickEvents");

        builder.HasKey(click => click.Id);

        // Deleting a link (an expired alias being reclaimed) deletes its click history with it
        // (research R6).
        builder.HasOne<ShortLink>()
            .WithMany()
            .HasForeignKey(click => click.ShortLinkId)
            .OnDelete(DeleteBehavior.Cascade);

        // Serves the per-day statistics query and the foreign key (research R7).
        builder.HasIndex(click => new { click.ShortLinkId, click.OccurredAtUtc })
            .HasDatabaseName("IX_ClickEvents_ShortLinkId_OccurredAtUtc");

        // SQLite stores DateTime as text and reads it back as Unspecified; mark it as UTC.
        builder.Property(click => click.OccurredAtUtc)
            .IsRequired()
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        builder.Property(click => click.ReferrerHost)
            .HasMaxLength(ReferrerHost.MaxLength);

        builder.Property(click => click.UserAgentFamily)
            .IsRequired()
            .HasMaxLength(UserAgentFamily.MaxLength);
    }
}