using System.Security.Cryptography;
using Estacionamento.Application.Idempotency;

namespace Estacionamento.UnitTests;

[TestClass]
public sealed class CheckInContentHmacTests
{
    [TestMethod]
    public void Idempotency_key_requires_a_non_empty_uuid_in_canonical_header_format()
    {
        var expected = Guid.NewGuid();
        Assert.IsTrue(IdempotencyKey.TryParse(expected.ToString("D"), out var key));
        Assert.AreEqual(expected, key.Value);
        Assert.IsFalse(IdempotencyKey.TryParse(null, out _));
        Assert.IsFalse(IdempotencyKey.TryParse("not-a-guid", out _));
        Assert.IsFalse(IdempotencyKey.TryParse(Guid.Empty.ToString("D"), out _));
        Assert.IsFalse(IdempotencyKey.TryParse(expected.ToString("N"), out _));
    }

    [TestMethod]
    public void Canonicalization_uses_normalized_effective_values_and_is_deterministic()
    {
        var spaceId = Guid.NewGuid();
        var first = CanonicalCheckInContent.Create(
            " ab-123 ", "  Model X ", " Owner Name ", " 123 456 ", " left door scratched ", spaceId);
        var equivalent = CanonicalCheckInContent.Create(
            "AB123", "Model X", "Owner Name", "123 456", "left door scratched", spaceId);
        var hasher = CreateHasher(1, (1, RandomNumberGenerator.GetBytes(32)));

        var firstDigest = hasher.Compute(first);
        var equivalentDigest = hasher.Compute(equivalent);

        Assert.AreEqual(firstDigest.KeyVersion, equivalentDigest.KeyVersion);
        CollectionAssert.AreEqual(firstDigest.Hmac, equivalentDigest.Hmac);
        Assert.AreEqual(32, firstDigest.Hmac.Length);
    }

    [TestMethod]
    public void Reusing_key_with_different_normalized_content_does_not_match()
    {
        var spaceId = Guid.NewGuid();
        var original = CanonicalCheckInContent.Create("AB-123", "Model", "Owner", "doc-1", "inspection", spaceId);
        var changed = CanonicalCheckInContent.Create("AB-123", "Model", "Owner", "doc-2", "inspection", spaceId);
        var hasher = CreateHasher(1, (1, RandomNumberGenerator.GetBytes(32)));
        var digest = hasher.Compute(original);

        Assert.IsTrue(hasher.Matches(original, digest.KeyVersion, digest.Hmac));
        Assert.IsFalse(hasher.Matches(changed, digest.KeyVersion, digest.Hmac));
        Assert.IsFalse(hasher.Matches(original, digest.KeyVersion, new byte[32]));
    }

    [TestMethod]
    public void Historical_secret_version_remains_comparable_after_rotation()
    {
        var keyV1 = RandomNumberGenerator.GetBytes(32);
        var keyV2 = RandomNumberGenerator.GetBytes(32);
        var content = CanonicalCheckInContent.Create("ABC123", "Model", "Owner", "doc", "inspection", Guid.NewGuid());
        var beforeRotation = new CheckInContentHmac(1, new Dictionary<int, byte[]> { [1] = keyV1 });
        var recorded = beforeRotation.Compute(content);
        var afterRotation = new CheckInContentHmac(2, new Dictionary<int, byte[]> { [1] = keyV1, [2] = keyV2 });
        var newDigest = afterRotation.Compute(content);

        Assert.AreEqual(2, newDigest.KeyVersion);
        Assert.IsTrue(afterRotation.Matches(content, recorded.KeyVersion, recorded.Hmac));
        Assert.IsFalse(afterRotation.Matches(content, 99, recorded.Hmac));
        Assert.IsFalse(afterRotation.Matches(content, 1, newDigest.Hmac));
    }

    [TestMethod]
    public void Invalid_or_missing_historical_keys_are_rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CheckInContentHmac(2, new Dictionary<int, byte[]>
        {
            [1] = RandomNumberGenerator.GetBytes(32)
        }));
        Assert.ThrowsExactly<ArgumentException>(() => CreateHasher(1, (1, new byte[16])));
    }

    private static CheckInContentHmac CreateHasher(int activeVersion, params (int Version, byte[] Key)[] keys) =>
        new(activeVersion, keys.ToDictionary(pair => pair.Version, pair => pair.Key));
}
