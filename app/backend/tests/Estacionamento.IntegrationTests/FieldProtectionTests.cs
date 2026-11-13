using Estacionamento.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class FieldProtectionTests
{
    [TestMethod]
    public void Protected_field_is_ciphertext_bound_to_field_and_aggregate_and_survives_restart()
    {
        var path = CreateKeyRing();
        try
        {
            var aggregateId = Guid.NewGuid();
            var protector = CreateProtector(path);
            const string document = "synthetic-document-8842";
            var envelope = protector.Protect(document, aggregateId, "document", 1);

            Assert.IsFalse(envelope.Contains(document, StringComparison.Ordinal));
            Assert.AreEqual(document, CreateProtector(path).Unprotect(envelope, aggregateId, "document", 1));

            var services = new ServiceCollection();
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(path));
            using (var provider = services.BuildServiceProvider())
            {
                provider.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));
                var afterRotation = new DataProtectionFieldProtector(provider.GetRequiredService<IDataProtectionProvider>());
                Assert.AreEqual(document, afterRotation.Unprotect(envelope, aggregateId, "document", 1));
            }

            Assert.ThrowsExactly<System.Security.Cryptography.CryptographicException>(
                () => protector.Unprotect(envelope, aggregateId, "entry-inspection", 1));
            Assert.ThrowsExactly<System.Security.Cryptography.CryptographicException>(
                () => protector.Unprotect(envelope, Guid.NewGuid(), "document", 1));
            Assert.ThrowsExactly<FormatException>(() => protector.Unprotect(envelope, aggregateId, "document", 2));
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [TestMethod]
    public void Missing_historical_key_fails_instead_of_returning_empty_content()
    {
        var sourcePath = CreateKeyRing();
        var emptyPath = CreateKeyRing();
        try
        {
            var aggregateId = Guid.NewGuid();
            var envelope = CreateProtector(sourcePath).Protect("synthetic inspection", aggregateId, "entry-inspection", 1);
            Assert.ThrowsExactly<System.Security.Cryptography.CryptographicException>(
                () => CreateProtector(emptyPath).Unprotect(envelope, aggregateId, "entry-inspection", 1));
        }
        finally
        {
            Directory.Delete(sourcePath, recursive: true);
            Directory.Delete(emptyPath, recursive: true);
        }
    }

    private static DataProtectionFieldProtector CreateProtector(string path) =>
        new(DataProtectionProvider.Create(new DirectoryInfo(path)));

    private static string CreateKeyRing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"estacionamento-fields-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}
