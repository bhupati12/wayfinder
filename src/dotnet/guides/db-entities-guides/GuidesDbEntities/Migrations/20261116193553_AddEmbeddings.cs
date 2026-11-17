using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace GuidesDbEntities.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "embedded_at",
                table: "guide_chunks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "embedded_content_hash",
                table: "guide_chunks",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "embedding",
                table: "guide_chunks",
                type: "vector(768)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "embedding_model",
                table: "guide_chunks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "embedding_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    chunks_pending = table.Column<int>(type: "integer", nullable: false),
                    chunks_embedded = table.Column<int>(type: "integer", nullable: false),
                    chunks_failed = table.Column<int>(type: "integer", nullable: false),
                    requests = table.Column<int>(type: "integer", nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    tokens_reported_by_provider = table.Column<bool>(type: "boolean", nullable: false),
                    estimated_cost_usd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_embedding_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guide_chunks_embedding",
                table: "guide_chunks",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" })
                .Annotation("Npgsql:StorageParameter:ef_construction", 64)
                .Annotation("Npgsql:StorageParameter:m", 16);

            migrationBuilder.CreateIndex(
                name: "ix_embedding_runs_started_at",
                table: "embedding_runs",
                column: "started_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "embedding_runs");

            migrationBuilder.DropIndex(
                name: "ix_guide_chunks_embedding",
                table: "guide_chunks");

            migrationBuilder.DropColumn(
                name: "embedded_at",
                table: "guide_chunks");

            migrationBuilder.DropColumn(
                name: "embedded_content_hash",
                table: "guide_chunks");

            migrationBuilder.DropColumn(
                name: "embedding",
                table: "guide_chunks");

            migrationBuilder.DropColumn(
                name: "embedding_model",
                table: "guide_chunks");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
