namespace AppointmentScheduler.BuildingBlocks.SharedKernel;

/// <summary>
/// Marks a type as a <b>value object</b> — immutable, self-validating, and compared by
/// <b>structural equality</b> (equal when all their components are equal, with no identity of their
/// own). A marker interface: it carries no members because <c>record</c> already supplies the
/// equality, hashing, and immutability a value object needs. Its purpose is to document intent and
/// let the architecture tests assert that domain primitives are value objects rather than entities.
/// Model these as <c>sealed record</c>s, never as <see cref="Entity{TId}"/>.
/// </summary>
public interface IValueObject;
