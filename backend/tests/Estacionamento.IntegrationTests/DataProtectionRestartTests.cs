using Microsoft.AspNetCore.DataProtection;

namespace Estacionamento.IntegrationTests;

[TestClass]
public sealed class DataProtectionRestartTests
{
    [TestMethod]
    public void Persisted_key_ring_can_read_existing_protected_payload_after_provider_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"estacionamento-keyring-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var directory = new DirectoryInfo(path);
            var firstProtector = DataProtectionProvider.Create(directory)
                .CreateProtector("GestaoEstacionamento.IntegrationTest");
            var protectedPayload = firstProtector.Protect("session-survives-restart");

            var restartedProtector = DataProtectionProvider.Create(directory)
                .CreateProtector("GestaoEstacionamento.IntegrationTest");

            Assert.AreEqual("session-survives-restart", restartedProtector.Unprotect(protectedPayload));
            if (!OperatingSystem.IsWindows())
            {
                var permissions = File.GetUnixFileMode(path);
                Assert.AreEqual(0, (int)(permissions & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite
                    | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite
                    | UnixFileMode.OtherExecute)));
            }
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
