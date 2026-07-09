namespace AppointmentScheduler.Booking.Domain;

/// <summary>
/// A half-open scheduling window <c>[Start, End)</c> — a value object (equal by value, immutable,
/// self-validating). Encapsulates the BR-03 overlap rule so "do these windows conflict?" has a
/// single definition shared by the domain, the EF query, and the tests.
/// </summary>
public sealed record TimeSlot
{
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }

    public TimeSlot(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new ArgumentException(
                $"A time slot must end after it starts (start={start:o}, end={end:o}).", nameof(end));
        }

        Start = start;
        End = end;
    }

    /// <summary>Builds the window <c>[start, start + duration)</c>.</summary>
    public static TimeSlot FromDuration(DateTimeOffset start, TimeSpan duration) => new(start, start + duration);

    public TimeSpan Duration => End - Start;

    /// <summary>
    /// BR-03 half-open overlap: <c>[Start, End)</c> intersects <c>[other.Start, other.End)</c> iff
    /// <c>Start &lt; other.End &amp;&amp; End &gt; other.Start</c>. Touching at an endpoint does not conflict.
    /// </summary>
    public bool Overlaps(TimeSlot other) => Start < other.End && End > other.Start;
}
