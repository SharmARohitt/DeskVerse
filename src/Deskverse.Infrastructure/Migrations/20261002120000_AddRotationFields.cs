using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deskverse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRotationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RotationEnabled",
                table: "Preferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RotationIntervalSeconds",
                table: "Preferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3600);

            migrationBuilder.AddColumn<int>(
                name: "RotationMode",
                table: "Preferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "RotationCollectionId",
                table: "Preferences",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastActiveWallpaperId",
                table: "Preferences",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RotationEnabled", table: "Preferences");
            migrationBuilder.DropColumn(name: "RotationIntervalSeconds", table: "Preferences");
            migrationBuilder.DropColumn(name: "RotationMode", table: "Preferences");
            migrationBuilder.DropColumn(name: "RotationCollectionId", table: "Preferences");
            migrationBuilder.DropColumn(name: "LastActiveWallpaperId", table: "Preferences");
        }
    }
}
