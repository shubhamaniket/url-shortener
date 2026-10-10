using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrlShortener.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShortLinks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    NormalizedCode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    IsCustomAlias = table.Column<bool>(type: "INTEGER", nullable: false),
                    OriginalUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClickCount = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShortLinks_NormalizedCode",
                table: "ShortLinks",
                column: "NormalizedCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShortLinks");
        }
    }
}