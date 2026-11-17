using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuidesDbEntities.Migrations
{
    /// <summary>
    /// Accent-insensitive keyword search. Creates the text search configuration
    /// <c>wayfinder_english</c> (a copy of <c>english</c> that runs <c>unaccent</c> before the
    /// English stemmer) and rebuilds both generated tsvector columns and their GIN indexes with it.
    /// The SQL is written out because EF cannot change the expression of a generated column in
    /// place; PostgreSQL needs the column dropped and added again.
    /// </summary>
    public partial class AddUnaccentSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");
            migrationBuilder.Sql(
                """
                CREATE TEXT SEARCH CONFIGURATION wayfinder_english (COPY = english);
                ALTER TEXT SEARCH CONFIGURATION wayfinder_english
                    ALTER MAPPING FOR hword, hword_part, word WITH unaccent, english_stem;
                """
            );
            Rebuild(migrationBuilder, "wayfinder_english");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Rebuild(migrationBuilder, "english");
            migrationBuilder.Sql("DROP TEXT SEARCH CONFIGURATION IF EXISTS wayfinder_english;");
        }

        private static void Rebuild(MigrationBuilder migrationBuilder, string config)
        {
            migrationBuilder.Sql(
                $"""
                DROP INDEX IF EXISTS ix_guide_chunks_search_vector;
                ALTER TABLE guide_chunks DROP COLUMN search_vector;
                ALTER TABLE guide_chunks ADD COLUMN search_vector tsvector
                    GENERATED ALWAYS AS (to_tsvector('{config}'::regconfig, section_heading || ' ' || text)) STORED;
                CREATE INDEX ix_guide_chunks_search_vector ON guide_chunks USING GIN (search_vector);

                DROP INDEX IF EXISTS ix_guide_articles_title_vector;
                ALTER TABLE guide_articles DROP COLUMN title_vector;
                ALTER TABLE guide_articles ADD COLUMN title_vector tsvector
                    GENERATED ALWAYS AS (to_tsvector('{config}'::regconfig, title)) STORED;
                CREATE INDEX ix_guide_articles_title_vector ON guide_articles USING GIN (title_vector);
                """
            );
        }
    }
}
