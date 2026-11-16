using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GuidesDbEntities.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guide_articles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    page_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    raw_text_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    processing_signature = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    revision_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_ingested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guide_articles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ingestion_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    articles_added = table.Column<int>(type: "integer", nullable: false),
                    articles_updated = table.Column<int>(type: "integer", nullable: false),
                    articles_skipped = table.Column<int>(type: "integer", nullable: false),
                    articles_failed = table.Column<int>(type: "integer", nullable: false),
                    chunks_written = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingestion_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guide_chunks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    article_id = table.Column<long>(type: "bigint", nullable: false),
                    section_heading = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guide_chunks", x => x.id);
                    table.ForeignKey(
                        name: "fk_guide_chunks_guide_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "guide_articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guide_articles_page_id",
                table: "guide_articles",
                column: "page_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guide_articles_title",
                table: "guide_articles",
                column: "title");

            migrationBuilder.CreateIndex(
                name: "ix_guide_chunks_article_id_position",
                table: "guide_chunks",
                columns: new[] { "article_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guide_chunks_content_hash",
                table: "guide_chunks",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ix_ingestion_runs_started_at",
                table: "ingestion_runs",
                column: "started_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guide_chunks");

            migrationBuilder.DropTable(
                name: "ingestion_runs");

            migrationBuilder.DropTable(
                name: "guide_articles");
        }
    }
}
