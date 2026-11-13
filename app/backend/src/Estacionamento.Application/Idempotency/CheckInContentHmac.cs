using System.Security.Cryptography;
using System.Text.Json;
using Estacionamento.Domain.Parking;

namespace Estacionamento.Application.Idempotency;

public readonly record struct IdempotencyKey(Guid Value)
{
    public static bool TryParse(string? value, out IdempotencyKey key)
    {
        if (Guid.TryParseExact(value, "D", out var parsed) && parsed != Guid.Empty)
        {
            key = new IdempotencyKey(parsed);
            return true;
        }

        key = default;
        return false;
    }
}

public sealed record CanonicalCheckInContent(
    string Plate,
    string Model,
    string OwnerName,
    string DocumentNumber,
    string EntryInspection,
    Guid ParkingSpaceId)
{
    public const int CanonicalizationVersion = 1;

    public static CanonicalCheckInContent Create(
        string? plate,
        string? model,
        string? ownerName,
        string? documentNumber,
        string? entryInspection,
        Guid parkingSpaceId)
    {
        if (parkingSpaceId == Guid.Empty)
        {
            throw new ArgumentException("A parking space is required.", nameof(parkingSpaceId));
        }

        var attendance = Attendance.Create(plate, model, ownerName, documentNumber);
        var inspection = Estacionamento.Domain.Parking.EntryInspection.Create(
            entryInspection, Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        return new CanonicalCheckInContent(
            attendance.Plate.Value,
            attendance.Model,
            attendance.OwnerName,
            attendance.DocumentNumber,
            inspection.Text,
            parkingSpaceId);
    }

    internal byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("canonicalizationVersion", CanonicalizationVersion);
            writer.WriteString("plate", Plate);
            writer.WriteString("model", Model);
            writer.WriteString("ownerName", OwnerName);
            writer.WriteString("documentNumber", DocumentNumber);
            writer.WriteString("entryInspection", EntryInspection);
            writer.WriteString("parkingSpaceId", ParkingSpaceId.ToString("D"));
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}

public sealed record CheckInContentDigest(int KeyVersion, byte[] Hmac);

public interface ICheckInContentHmac
{
    int ActiveKeyVersion { get; }
    CheckInContentDigest Compute(CanonicalCheckInContent content);
    bool Matches(CanonicalCheckInContent content, int keyVersion, ReadOnlySpan<byte> expectedHmac);
}

public sealed class CheckInContentHmac : ICheckInContentHmac
{
    private readonly IReadOnlyDictionary<int, byte[]> keys;

    public CheckInContentHmac(int activeKeyVersion, IReadOnlyDictionary<int, byte[]> keys)
    {
        if (activeKeyVersion <= 0 || !keys.ContainsKey(activeKeyVersion))
        {
            throw new ArgumentException("The active HMAC key version must be present in the key ring.", nameof(activeKeyVersion));
        }

        if (keys.Any(pair => pair.Key <= 0 || pair.Value.Length < 32))
        {
            throw new ArgumentException("Every HMAC key version must be positive and contain at least 256 bits.", nameof(keys));
        }

        ActiveKeyVersion = activeKeyVersion;
        this.keys = keys.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    public int ActiveKeyVersion { get; }

    public CheckInContentDigest Compute(CanonicalCheckInContent content)
    {
        var hmac = HMACSHA256.HashData(keys[ActiveKeyVersion], content.Serialize());
        return new CheckInContentDigest(ActiveKeyVersion, hmac);
    }

    public bool Matches(CanonicalCheckInContent content, int keyVersion, ReadOnlySpan<byte> expectedHmac)
    {
        return keys.TryGetValue(keyVersion, out var key)
            && expectedHmac.Length == 32
            && CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, content.Serialize()), expectedHmac);
    }
}
