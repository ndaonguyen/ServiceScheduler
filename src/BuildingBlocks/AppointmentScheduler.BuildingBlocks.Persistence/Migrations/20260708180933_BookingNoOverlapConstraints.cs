using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppointmentScheduler.BuildingBlocks.Persistence.Migrations
{
    /// <summary>
    /// Enforces the no-double-booking guarantee at the database level (NFR-01 / AC-03) via two
    /// <c>EXCLUDE USING gist</c> constraints on <c>appointments</c>. These are raw SQL because EXCLUDE
    /// constraints and CREATE EXTENSION have no EF Core / Npgsql fluent-mapping equivalent, so the EF
    /// model and snapshot are intentionally unchanged. <c>tstzrange(scheduled_start, scheduled_end)</c>
    /// uses the default <c>'[)'</c> bounds (half-open, BR-03: touching at T does not conflict) and the
    /// partial <c>WHERE status = 'Confirmed'</c> means non-confirmed statuses never trip it.
    /// </summary>
    public partial class BookingNoOverlapConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Extension first (supplies the gist operator class for scalar uuid `=`).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            // BR-01/BR-03: no two confirmed appointments may overlap on the same service bay.
            migrationBuilder.Sql("""
                ALTER TABLE appointments
                ADD CONSTRAINT ex_appointments_bay_no_overlap
                EXCLUDE USING gist (service_bay_id WITH =, tstzrange(scheduled_start, scheduled_end) WITH &&)
                WHERE (status = 'Confirmed');
                """);

            // BR-02/BR-03: no two confirmed appointments may overlap for the same technician.
            migrationBuilder.Sql("""
                ALTER TABLE appointments
                ADD CONSTRAINT ex_appointments_technician_no_overlap
                EXCLUDE USING gist (technician_id WITH =, tstzrange(scheduled_start, scheduled_end) WITH &&)
                WHERE (status = 'Confirmed');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the constraints; leave btree_gist installed (dropping it could affect other objects).
            migrationBuilder.Sql("ALTER TABLE appointments DROP CONSTRAINT IF EXISTS ex_appointments_technician_no_overlap;");
            migrationBuilder.Sql("ALTER TABLE appointments DROP CONSTRAINT IF EXISTS ex_appointments_bay_no_overlap;");
        }
    }
}
