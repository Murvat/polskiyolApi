using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    public partial class FixConversationTeacherRelationship : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep TeacherId as the canonical relationship and stop if the old
            // inverse navigation points to a different teacher.
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM [Conversations]
                    WHERE [TeacherId1] IS NOT NULL AND [TeacherId1] <> [TeacherId]
                )
                BEGIN
                    THROW 51000, 'Resolve conflicting conversation teacher assignments before removing TeacherId1.', 1;
                END;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Teachers_TeacherId1",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_TeacherId1",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "TeacherId1",
                table: "Conversations");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TeacherId1",
                table: "Conversations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_TeacherId1",
                table: "Conversations",
                column: "TeacherId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Teachers_TeacherId1",
                table: "Conversations",
                column: "TeacherId1",
                principalTable: "Teachers",
                principalColumn: "Id");
        }
    }
}
