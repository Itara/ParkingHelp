using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ParkingHelp.Migrations
{
    /// <inheritdoc />
    public partial class InitMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "member",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    member_login_id = table.Column<string>(type: "text", nullable: false),
                    member_name = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    create_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    slack_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "member_car",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    member_id = table.Column<int>(type: "integer", nullable: false),
                    car_number = table.Column<string>(type: "text", nullable: false),
                    create_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    update_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_car", x => x.id);
                    table.ForeignKey(
                        name: "FK_member_car_member_member_id",
                        column: x => x.member_id,
                        principalTable: "member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "help_offer",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    helper_mem_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    helper_service_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    discount_total_count = table.Column<int>(type: "integer", nullable: false),
                    discount_apply_count = table.Column<int>(type: "integer", nullable: true),
                    slack_thread_ts = table.Column<string>(type: "text", nullable: true),
                    MemberCarModelId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_help_offer", x => x.id);
                    table.ForeignKey(
                        name: "FK_help_offer_member_car_MemberCarModelId",
                        column: x => x.MemberCarModelId,
                        principalTable: "member_car",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_help_offer_member_helper_mem_id",
                        column: x => x.helper_mem_id,
                        principalTable: "member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "req_help",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    help_req_mem_id = table.Column<int>(type: "integer", nullable: false),
                    req_car_id = table.Column<int>(type: "integer", nullable: true),
                    discount_total_count = table.Column<int>(type: "integer", nullable: false),
                    discount_apply_count = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    req_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    slack_thread_ts = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_req_help", x => x.id);
                    table.ForeignKey(
                        name: "FK_req_help_member_car_req_car_id",
                        column: x => x.req_car_id,
                        principalTable: "member_car",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_req_help_member_help_req_mem_id",
                        column: x => x.help_req_mem_id,
                        principalTable: "member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "help_offer_detail",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    help_offer_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    request_mem_id = table.Column<int>(type: "integer", nullable: true),
                    discount_apply_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    discount_apply_type = table.Column<int>(type: "integer", nullable: false),
                    request_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    slack_thread_ts = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_help_offer_detail", x => x.id);
                    table.ForeignKey(
                        name: "FK_help_offer_detail_help_offer_help_offer_id",
                        column: x => x.help_offer_id,
                        principalTable: "help_offer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_help_offer_detail_member_request_mem_id",
                        column: x => x.request_mem_id,
                        principalTable: "member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "req_help_detail",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    req_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    helper_mem_id = table.Column<int>(type: "integer", nullable: true),
                    discount_apply_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    discount_apply_type = table.Column<int>(type: "integer", nullable: false),
                    insert_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    slack_thread_ts = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_req_help_detail", x => x.id);
                    table.ForeignKey(
                        name: "FK_req_help_detail_member_helper_mem_id",
                        column: x => x.helper_mem_id,
                        principalTable: "member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_req_help_detail_req_help_req_id",
                        column: x => x.req_id,
                        principalTable: "req_help",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_help_offer_helper_mem_id",
                table: "help_offer",
                column: "helper_mem_id");

            migrationBuilder.CreateIndex(
                name: "IX_help_offer_MemberCarModelId",
                table: "help_offer",
                column: "MemberCarModelId");

            migrationBuilder.CreateIndex(
                name: "IX_help_offer_detail_help_offer_id",
                table: "help_offer_detail",
                column: "help_offer_id");

            migrationBuilder.CreateIndex(
                name: "IX_help_offer_detail_request_mem_id",
                table: "help_offer_detail",
                column: "request_mem_id");

            migrationBuilder.CreateIndex(
                name: "IX_member_car_member_id",
                table: "member_car",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_req_help_help_req_mem_id",
                table: "req_help",
                column: "help_req_mem_id");

            migrationBuilder.CreateIndex(
                name: "IX_req_help_req_car_id",
                table: "req_help",
                column: "req_car_id");

            migrationBuilder.CreateIndex(
                name: "IX_req_help_detail_helper_mem_id",
                table: "req_help_detail",
                column: "helper_mem_id");

            migrationBuilder.CreateIndex(
                name: "IX_req_help_detail_req_id",
                table: "req_help_detail",
                column: "req_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "help_offer_detail");

            migrationBuilder.DropTable(
                name: "req_help_detail");

            migrationBuilder.DropTable(
                name: "help_offer");

            migrationBuilder.DropTable(
                name: "req_help");

            migrationBuilder.DropTable(
                name: "member_car");

            migrationBuilder.DropTable(
                name: "member");
        }
    }
}
