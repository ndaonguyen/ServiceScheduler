using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppointmentScheduler.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "booking");

            migrationBuilder.EnsureSchema(
                name: "fleet");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "workforce");

            migrationBuilder.RenameTable(
                name: "vehicles",
                newName: "vehicles",
                newSchema: "fleet");

            migrationBuilder.RenameTable(
                name: "technicians",
                newName: "technicians",
                newSchema: "workforce");

            migrationBuilder.RenameTable(
                name: "technician_qualifications",
                newName: "technician_qualifications",
                newSchema: "workforce");

            migrationBuilder.RenameTable(
                name: "service_types",
                newName: "service_types",
                newSchema: "catalog");

            migrationBuilder.RenameTable(
                name: "service_bays",
                newName: "service_bays",
                newSchema: "fleet");

            migrationBuilder.RenameTable(
                name: "dealerships",
                newName: "dealerships",
                newSchema: "fleet");

            migrationBuilder.RenameTable(
                name: "appointments",
                newName: "appointments",
                newSchema: "booking");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "vehicles",
                schema: "fleet",
                newName: "vehicles");

            migrationBuilder.RenameTable(
                name: "technicians",
                schema: "workforce",
                newName: "technicians");

            migrationBuilder.RenameTable(
                name: "technician_qualifications",
                schema: "workforce",
                newName: "technician_qualifications");

            migrationBuilder.RenameTable(
                name: "service_types",
                schema: "catalog",
                newName: "service_types");

            migrationBuilder.RenameTable(
                name: "service_bays",
                schema: "fleet",
                newName: "service_bays");

            migrationBuilder.RenameTable(
                name: "dealerships",
                schema: "fleet",
                newName: "dealerships");

            migrationBuilder.RenameTable(
                name: "appointments",
                schema: "booking",
                newName: "appointments");
        }
    }
}
