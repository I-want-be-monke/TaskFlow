using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace TaskFlow.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TaskFlowDbContext))]
[Migration("20260924170000_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "auth_users",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                normalized_user_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                password_hash = table.Column<string>(type: "text", nullable: false),
                security_stamp = table.Column<string>(type: "text", nullable: false),
                concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                phone_number = table.Column<string>(type: "text", nullable: true),
                phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                access_failed_count = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_auth_users", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "data_protection_keys",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                friendly_name = table.Column<string>(type: "text", nullable: true),
                xml = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_data_protection_keys", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "auth_user_claims",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                claim_type = table.Column<string>(type: "text", nullable: true),
                claim_value = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_auth_user_claims", x => x.id);
                table.ForeignKey(
                    name: "fk_auth_user_claims_auth_users_user_id",
                    column: x => x.user_id,
                    principalTable: "auth_users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "auth_user_logins",
            columns: table => new
            {
                login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                provider_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                provider_display_name = table.Column<string>(type: "text", nullable: true),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_auth_user_logins", x => new { x.login_provider, x.provider_key });
                table.ForeignKey(
                    name: "fk_auth_user_logins_auth_users_user_id",
                    column: x => x.user_id,
                    principalTable: "auth_users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "auth_user_tokens",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                value = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_auth_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                table.ForeignKey(
                    name: "fk_auth_user_tokens_auth_users_user_id",
                    column: x => x.user_id,
                    principalTable: "auth_users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "projects",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_projects", x => x.id);
                table.CheckConstraint("ck_projects_name_non_empty", "char_length(name) >= 1");
                table.CheckConstraint("ck_projects_status", "status IN ('Active', 'Archived')");
                table.CheckConstraint("ck_projects_updated_at", "updated_at >= created_at");
                table.CheckConstraint("ck_projects_version", "version >= 1");
                table.ForeignKey(
                    name: "fk_projects_auth_users_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "auth_users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "tags",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                normalized_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_tags", x => x.id);
                table.CheckConstraint("ck_tags_name_non_empty", "char_length(name) >= 1");
                table.CheckConstraint("ck_tags_updated_at", "updated_at >= created_at");
                table.CheckConstraint("ck_tags_version", "version >= 1");
                table.ForeignKey(
                    name: "fk_tags_auth_users_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "auth_users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "task_items",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                priority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_task_items", x => x.id);
                table.CheckConstraint("ck_task_items_title_non_empty", "char_length(title) >= 1");
                table.CheckConstraint("ck_task_items_status", "status IN ('Todo', 'InProgress', 'Done')");
                table.CheckConstraint("ck_task_items_priority", "priority IN ('Low', 'Medium', 'High')");
                table.CheckConstraint("ck_task_items_updated_at", "updated_at >= created_at");
                table.CheckConstraint("ck_task_items_version", "version >= 1");
                table.ForeignKey(
                    name: "fk_task_items_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "task_tags",
            columns: table => new
            {
                task_id = table.Column<Guid>(type: "uuid", nullable: false),
                tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_task_tags", x => new { x.task_id, x.tag_id });
                table.ForeignKey(
                    name: "fk_task_tags_tags_tag_id",
                    column: x => x.tag_id,
                    principalTable: "tags",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_task_tags_task_items_task_id",
                    column: x => x.task_id,
                    principalTable: "task_items",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ux_auth_users_normalized_user_name",
            table: "auth_users",
            column: "normalized_user_name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_auth_users_normalized_email",
            table: "auth_users",
            column: "normalized_email");

        migrationBuilder.CreateIndex(
            name: "ix_auth_user_claims_user_id",
            table: "auth_user_claims",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_auth_user_logins_user_id",
            table: "auth_user_logins",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_projects_owner_user_id_status_created_at",
            table: "projects",
            columns: new[] { "owner_user_id", "status", "created_at" },
            descending: new[] { false, false, true });

        migrationBuilder.CreateIndex(
            name: "ix_task_items_project_id",
            table: "task_items",
            column: "project_id");

        migrationBuilder.CreateIndex(
            name: "ix_task_items_project_id_status",
            table: "task_items",
            columns: new[] { "project_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_task_items_project_id_priority",
            table: "task_items",
            columns: new[] { "project_id", "priority" });

        migrationBuilder.CreateIndex(
            name: "ix_task_items_due_at",
            table: "task_items",
            column: "due_at",
            filter: "\"due_at\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ux_tags_owner_user_id_normalized_name",
            table: "tags",
            columns: new[] { "owner_user_id", "normalized_name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_tags_owner_user_id_name",
            table: "tags",
            columns: new[] { "owner_user_id", "name" });

        migrationBuilder.CreateIndex(
            name: "ix_task_tags_tag_id_task_id",
            table: "task_tags",
            columns: new[] { "tag_id", "task_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "auth_user_claims");
        migrationBuilder.DropTable(name: "auth_user_logins");
        migrationBuilder.DropTable(name: "auth_user_tokens");
        migrationBuilder.DropTable(name: "data_protection_keys");
        migrationBuilder.DropTable(name: "task_tags");
        migrationBuilder.DropTable(name: "task_items");
        migrationBuilder.DropTable(name: "tags");
        migrationBuilder.DropTable(name: "projects");
        migrationBuilder.DropTable(name: "auth_users");
    }
}
