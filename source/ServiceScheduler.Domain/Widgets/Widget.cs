namespace ServiceScheduler.Domain.Widgets;

/// <summary>A servicescheduler aggregate. Replace with your own domain entities.</summary>
public sealed class Widget
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public static Widget Create(string name, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        CreatedAt = now,
    };
}
