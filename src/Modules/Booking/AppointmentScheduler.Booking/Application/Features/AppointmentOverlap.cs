using System.Linq.Expressions;
using AppointmentScheduler.Booking.Domain;

namespace AppointmentScheduler.Booking.Application.Features;

/// <summary>
/// BR-03 half-open overlap as an EF-translatable predicate over the scalar schedule columns: an
/// appointment intersects the requested <c>[start, end)</c> iff
/// <c>ScheduledStart &lt; end &amp;&amp; ScheduledEnd &gt; start</c>. Mirrors <see cref="TimeSlot.Overlaps"/>
/// (which EF cannot translate as a method call) so the SQL query and the compiled unit tests share
/// one definition of "conflict".
/// </summary>
public static class AppointmentOverlap
{
    public static Expression<Func<Appointment, bool>> Within(DateTimeOffset start, DateTimeOffset end) =>
        a => a.ScheduledStart < end && a.ScheduledEnd > start;
}
