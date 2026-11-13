namespace Estacionamento.Domain.Parking;

public sealed record EntryInspection
{
    public const int MaximumTextLength = 4000;

    private EntryInspection(string text, Guid actorId, DateTimeOffset recordedAtUtc)
    {
        Text = text;
        ActorId = actorId;
        RecordedAtUtc = recordedAtUtc;
    }

    public string Text { get; }
    public Guid ActorId { get; }
    public DateTimeOffset RecordedAtUtc { get; }

    public static EntryInspection Create(string? text, Guid actorId, DateTimeOffset recordedAtUtc)
    {
        var normalized = text?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaximumTextLength)
        {
            throw new ArgumentException($"Inspection text must contain 1 to {MaximumTextLength} characters.", nameof(text));
        }

        if (actorId == Guid.Empty)
        {
            throw new ArgumentException("An actor is required.", nameof(actorId));
        }

        return new EntryInspection(normalized, actorId, recordedAtUtc.ToUniversalTime());
    }
}
