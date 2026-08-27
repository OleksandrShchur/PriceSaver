using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSaver.Server.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Baseline schema is applied via docs/sql/schema.sql (and 001/002 incremental scripts).
    /// This migration only adds refresh columns to an existing StoreLocations table.
    /// </remarks>
    public partial class AddStoreLocationExternalIdAndLastRefreshedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "StoreLocations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRefreshedAt",
                table: "StoreLocations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreLocations_StoreType_ExternalId",
                table: "StoreLocations",
                columns: new[] { "StoreType", "ExternalId" },
                unique: true,
                filter: "[ExternalId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StoreLocations_StoreType_ExternalId",
                table: "StoreLocations");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "StoreLocations");

            migrationBuilder.DropColumn(
                name: "LastRefreshedAt",
                table: "StoreLocations");
        }
    }
}
