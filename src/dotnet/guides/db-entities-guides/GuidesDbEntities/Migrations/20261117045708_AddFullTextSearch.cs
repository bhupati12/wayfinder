using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace GuidesDbEntities.Migrations
{
    /// <inheritdoc />
    public partial class AddFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "guide_chunks",
                type: "tsvector",
                nullable: true)
                .Annotation("Npgsql:TsVectorConfig", "english")
                .Annotation("Npgsql:TsVectorProperties", new[] { "section_heading", "text" });

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "title_vector",
                table: "guide_articles",
                type: "tsvector",
                nullable: true)
                .Annotation("Npgsql:TsVectorConfig", "english")
                .Annotation("Npgsql:TsVectorProperties", new[] { "title" });

            migrationBuilder.CreateIndex(
                name: "ix_guide_chunks_search_vector",
                table: "guide_chunks",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_guide_articles_title_vector",
                table: "guide_articles",
                column: "title_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_guide_chunks_search_vector",
                table: "guide_chunks");

            migrationBuilder.DropIndex(
                name: "ix_guide_articles_title_vector",
                table: "guide_articles");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "guide_chunks");

            migrationBuilder.DropColumn(
                name: "title_vector",
                table: "guide_articles");
        }
    }
}
