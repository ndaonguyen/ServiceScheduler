using System.Linq.Expressions;
using AppointmentScheduler.Booking.Domain;

namespace AppointmentScheduler.Booking.Application.Features;

/// <summary>
/// BR-03 half-open overlap: an appointment's <c>[ScheduledStart, ScheduledEnd)</c> intersects the
/// requested <c>[start, end)</c> iff <c>ScheduledStart &lt; end &amp;&amp; ScheduledEnd &gt; start</c>.
/// An appointment ending exactly at the requested start (or starting exactly at the requested end)
/// therefore does <b>not</b> conflict. Expressed once so the EF query (translated to SQL) and the unit
/// tests (compiled) share a single definition of "conflict".
/// </summary>
public static class AppointmentOverlap
{
    public static Expression<Func<Appointment, bool>> Within(DateTimeOffset start, DateTimeOffset end) =>
        a => a.ScheduledStart < end && a.ScheduledEnd > start;
}
