namespace Estacionamento.Domain.Parking;

public sealed record Attendance
{
    public const int MaximumModelLength = 120;
    public const int MaximumOwnerNameLength = 200;
    public const int MaximumDocumentLength = 80;

    private Attendance(Guid id, string publicIdentifier, VehiclePlate plate, string model, string ownerName, string documentNumber)
    {
        Id = id;
        PublicIdentifier = publicIdentifier;
        Plate = plate;
        Model = model;
        OwnerName = ownerName;
        DocumentNumber = documentNumber;
    }

    public Guid Id { get; }
    public string PublicIdentifier { get; }
    public VehiclePlate Plate { get; }
    public string Model { get; }
    public string OwnerName { get; }
    public string DocumentNumber { get; }

    public static Attendance Create(string? plate, string? model, string? ownerName, string? documentNumber)
    {
        var normalizedModel = RequiredText(model, MaximumModelLength, nameof(model));
        var normalizedOwner = RequiredText(ownerName, MaximumOwnerNameLength, nameof(ownerName));
        var normalizedDocument = RequiredText(documentNumber, MaximumDocumentLength, nameof(documentNumber));

        return new Attendance(Guid.NewGuid(), Guid.NewGuid().ToString("N"), VehiclePlate.Create(plate), normalizedModel, normalizedOwner, normalizedDocument);
    }

    private static string RequiredText(string? value, int maximumLength, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value must contain 1 to {maximumLength} characters.", parameterName);
        }

        return normalized;
    }
}
