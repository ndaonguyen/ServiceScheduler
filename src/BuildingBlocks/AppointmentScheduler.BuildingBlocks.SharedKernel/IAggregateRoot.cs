namespace AppointmentScheduler.BuildingBlocks.SharedKernel;

/// <summary>
/// Marks an entity as an <b>aggregate root</b> — a consistency boundary and the only entity in its
/// aggregate that outside code (repositories, other modules) may reference directly. A marker
/// interface: it carries no members, but it documents intent and lets the architecture tests enforce
/// aggregate rules (e.g. roots expose no public setters).
/// </summary>
public interface IAggregateRoot;
