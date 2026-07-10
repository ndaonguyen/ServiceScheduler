namespace AppointmentScheduler.BuildingBlocks.SharedKernel;

/// <summary>
/// Base class for domain entities: things with a stable <see cref="Id"/> and <b>identity equality</b>
/// (two entities are equal when they are the same type and share the same id, regardless of their
/// other fields). Value objects, by contrast, use structural equality — model those as records, not
/// as entities.
/// </summary>
/// <typeparam name="TId">The identity type (e.g. <see cref="System.Guid"/>).</typeparam>
public abstract class Entity<TId>
    where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other
        && GetType() == other.GetType()
        && EqualityComparer<TId>.Default.Equals(Id, other.Id);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
