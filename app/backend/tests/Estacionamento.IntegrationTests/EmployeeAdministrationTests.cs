using Estacionamento.Application.Abstractions;
using Estacionamento.Application.Employees;
using Estacionamento.Application.Security;
using Estacionamento.Infrastructure.Identity;
using Estacionamento.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using EmployeeRoles = Estacionamento.Infrastructure.Identity.EmployeeRoles;

namespace Estacionamento.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class EmployeeAdministrationTests
{
    private static readonly Guid AdministratorId = Guid.NewGuid();

    [TestMethod]
    public async Task Creation_validates_password_and_roles_and_never_returns_or_logs_password()
    {
        var connection = CreateIsolatedConnection();
        var logs = new TestLogs();
        try
        {
            await using var root = BuildProvider(connection, logs);
            await PrepareDatabaseAsync(root);
            await using var scope = root.CreateAsyncScope();
            var administration = scope.ServiceProvider.GetRequiredService<EmployeeAdministration>();

            var invalidPassword = await administration.CreateAsync(
                new CreateEmployeeCommand("invalid@example.test", "weak", [EmployeeRoles.Valet]), CancellationToken.None);
            var invalidRole = await administration.CreateAsync(
                new CreateEmployeeCommand("unknown-role@example.test", "Valid!Password2026", ["owner"]), CancellationToken.None);
            Assert.AreEqual(EmployeeCreateStatus.Invalid, invalidPassword.Status);
            Assert.AreEqual(EmployeeCreateStatus.Invalid, invalidRole.Status);

            const string password = "Valid!Password2026";
            var created = await administration.CreateAsync(
                new CreateEmployeeCommand("new-employee@example.test", password, [EmployeeRoles.Cashier]), CancellationToken.None);
            Assert.AreEqual(EmployeeCreateStatus.Created, created.Status);
            Assert.IsNotNull(created.Employee);
            var serialized = System.Text.Json.JsonSerializer.Serialize(created);
            Assert.IsFalse(serialized.Contains(password, StringComparison.Ordinal));
            Assert.IsFalse(string.Join("\n", logs.Messages).Contains(password, StringComparison.Ordinal));

            var manager = scope.ServiceProvider.GetRequiredService<UserManager<EmployeeAccount>>();
            var account = await manager.FindByEmailAsync("new-employee@example.test");
            Assert.IsNotNull(account);
            Assert.IsTrue(await manager.CheckPasswordAsync(account, password));
            Assert.IsTrue(await manager.IsInRoleAsync(account, EmployeeRoles.Cashier));
            Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuditEvents
                .CountAsync(item => item.Action == "employee.created" && item.TargetId == account.Id));
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Concurrent_duplicate_email_creates_one_account_and_one_audit_event()
    {
        var connection = CreateIsolatedConnection();
        try
        {
            await using var root = BuildProvider(connection, new TestLogs());
            await PrepareDatabaseAsync(root);
            async Task<EmployeeCreateResult> CreateAsync()
            {
                await using var scope = root.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<EmployeeAdministration>().CreateAsync(
                    new CreateEmployeeCommand("race@example.test", "Valid!Password2026", [EmployeeRoles.Valet]),
                    CancellationToken.None);
            }

            var results = await Task.WhenAll(CreateAsync(), CreateAsync());
            CollectionAssert.AreEquivalent(
                new[] { EmployeeCreateStatus.Created, EmployeeCreateStatus.DuplicateEmail },
                results.Select(result => result.Status).ToArray());
            await using var verifyScope = root.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.AreEqual(1, await db.Users.CountAsync(user => user.NormalizedEmail == "RACE@EXAMPLE.TEST"));
            Assert.AreEqual(1, await db.AuditEvents.CountAsync(item => item.Action == "employee.created"));
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Audit_failure_rolls_back_created_identity_account()
    {
        var connection = CreateIsolatedConnection();
        try
        {
            await using var root = BuildProvider(connection, new TestLogs(), failAudit: true);
            await PrepareDatabaseAsync(root);
            await using var scope = root.CreateAsyncScope();
            var administration = scope.ServiceProvider.GetRequiredService<EmployeeAdministration>();
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => administration.CreateAsync(
                new CreateEmployeeCommand("rollback@example.test", "Valid!Password2026", [EmployeeRoles.Valet]), CancellationToken.None));

            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.IsNull(await scope.ServiceProvider.GetRequiredService<UserManager<EmployeeAccount>>().FindByEmailAsync("rollback@example.test"));
            Assert.AreEqual(0, await db.AuditEvents.CountAsync(item => item.Action == "employee.created"));
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Access_update_preserves_account_rejects_stale_stamp_and_protects_last_administrator()
    {
        var connection = CreateIsolatedConnection();
        var employeeId = Guid.NewGuid();
        try
        {
            await using var root = BuildProvider(connection, new TestLogs());
            await PrepareDatabaseAsync(root);
            await using (var seedScope = root.CreateAsyncScope())
            {
                var db = seedScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                db.Users.Add(new EmployeeAccount
                {
                    Id = employeeId,
                    UserName = "staff@example.test",
                    NormalizedUserName = "STAFF@EXAMPLE.TEST",
                    Email = "staff@example.test",
                    NormalizedEmail = "STAFF@EXAMPLE.TEST",
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                    IsActive = true
                });
                await db.SaveChangesAsync();
                db.UserRoles.Add(new IdentityUserRole<Guid>
                {
                    UserId = employeeId,
                    RoleId = await db.Roles.Where(role => role.Name == EmployeeRoles.Valet).Select(role => role.Id).SingleAsync()
                });
                await db.SaveChangesAsync();
            }

            await using (var scope = root.CreateAsyncScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<EmployeeAccount>>();
                var employee = await manager.FindByIdAsync(employeeId.ToString());
                Assert.IsNotNull(employee);
                var originalStamp = employee.ConcurrencyStamp!;
                var useCase = scope.ServiceProvider.GetRequiredService<EmployeeAdministration>();
                var updated = await useCase.UpdateAccessAsync(
                    new UpdateEmployeeAccessCommand(employeeId, originalStamp, false, [EmployeeRoles.Valet]), CancellationToken.None);
                Assert.AreEqual(EmployeeAccessStatus.Updated, updated.Status);
                Assert.IsNotNull(updated.ConcurrencyStamp);
                var stale = await useCase.UpdateAccessAsync(
                    new UpdateEmployeeAccessCommand(employeeId, originalStamp, true, [EmployeeRoles.Administrator]), CancellationToken.None);
                Assert.AreEqual(EmployeeAccessStatus.Conflict, stale.Status);

                var administrator = await manager.FindByIdAsync(AdministratorId.ToString());
                Assert.IsNotNull(administrator);
                var lastAdmin = await useCase.UpdateAccessAsync(
                    new UpdateEmployeeAccessCommand(AdministratorId, administrator.ConcurrencyStamp!, false, [EmployeeRoles.Administrator]),
                    CancellationToken.None);
                Assert.AreEqual(EmployeeAccessStatus.LastAdministrator, lastAdmin.Status);
                Assert.IsNotNull(await manager.FindByIdAsync(employeeId.ToString()), "Deactivation must preserve the employee identity for historical authorship.");
                Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuditEvents
                    .CountAsync(item => item.Action == "employee.access_updated" && item.TargetId == employeeId));
            }
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [TestMethod]
    public async Task Concurrent_access_removals_cannot_remove_every_active_administrator()
    {
        var connection = CreateIsolatedConnection();
        var secondAdministratorId = Guid.NewGuid();
        try
        {
            await using var root = BuildProvider(connection, new TestLogs());
            await PrepareDatabaseAsync(root);
            Dictionary<Guid, string> stamps;
            await using (var seedScope = root.CreateAsyncScope())
            {
                var db = seedScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                db.Users.Add(new EmployeeAccount
                {
                    Id = secondAdministratorId,
                    UserName = "second-admin@example.test",
                    NormalizedUserName = "SECOND-ADMIN@EXAMPLE.TEST",
                    Email = "second-admin@example.test",
                    NormalizedEmail = "SECOND-ADMIN@EXAMPLE.TEST",
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                    IsActive = true
                });
                await db.SaveChangesAsync();
                db.UserRoles.Add(new IdentityUserRole<Guid>
                {
                    UserId = secondAdministratorId,
                    RoleId = await db.Roles.Where(role => role.Name == EmployeeRoles.Administrator).Select(role => role.Id).SingleAsync()
                });
                await db.SaveChangesAsync();
                stamps = await db.Users.Where(user => user.Id == AdministratorId || user.Id == secondAdministratorId)
                    .ToDictionaryAsync(user => user.Id, user => user.ConcurrencyStamp!);
            }

            async Task<EmployeeAccessResult> RemoveAsync(Guid id)
            {
                await using var scope = root.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<EmployeeAdministration>().UpdateAccessAsync(
                    new UpdateEmployeeAccessCommand(id, stamps[id], false, [EmployeeRoles.Administrator]), CancellationToken.None);
            }

            var results = await Task.WhenAll(RemoveAsync(AdministratorId), RemoveAsync(secondAdministratorId));
            CollectionAssert.AreEquivalent(
                new[] { EmployeeAccessStatus.Updated, EmployeeAccessStatus.LastAdministrator },
                results.Select(result => result.Status).ToArray());
            await using var verifyScope = root.CreateAsyncScope();
            var dbAfter = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var adminsRole = await dbAfter.Roles.Where(role => role.Name == EmployeeRoles.Administrator).Select(role => role.Id).SingleAsync();
            var activeAdmins = await (from user in dbAfter.Users
                                      join membership in dbAfter.UserRoles on user.Id equals membership.UserId
                                      where membership.RoleId == adminsRole && user.IsActive
                                      select user.Id).CountAsync();
            Assert.AreEqual(1, activeAdmins);
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    private static ServiceProvider BuildProvider(SqlConnectionStringBuilder connection, TestLogs logs, bool failAudit = false)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddDbContext<IdentityDbContext>(options => options.UseSqlServer(connection.ConnectionString));
        services.AddIdentityCore<EmployeeAccount>()
            .AddRoles<EmployeeRole>()
            .AddEntityFrameworkStores<IdentityDbContext>();
        services.AddSingleton<IUseCaseAuthorizer, RoleUseCaseAuthorizer>();
        services.AddSingleton<ICurrentActor>(new TestActor(AdministratorId, [EmployeeRoles.Administrator]));
        services.AddSingleton<IClock>(new TestClock());
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IEmployeeDirectory, EfEmployeeDirectory>();
        services.AddScoped<EmployeeAdministration>();
        services.AddScoped<EfAuditStore>();
        services.AddScoped<IAuditWriter>(provider => failAudit
            ? new FailingAuditWriter()
            : provider.GetRequiredService<EfAuditStore>());
        return services.BuildServiceProvider();
    }

    private static async Task PrepareDatabaseAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();
        foreach (var role in EmployeeRoles.All)
        {
            db.Roles.Add(new EmployeeRole { Id = Guid.NewGuid(), Name = role, NormalizedName = role.ToUpperInvariant() });
        }

        db.Users.Add(new EmployeeAccount
        {
            Id = AdministratorId,
            UserName = "admin@example.test",
            NormalizedUserName = "ADMIN@EXAMPLE.TEST",
            Email = "admin@example.test",
            NormalizedEmail = "ADMIN@EXAMPLE.TEST",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            IsActive = true
        });
        await db.SaveChangesAsync();
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = AdministratorId, RoleId = db.Roles.Single(role => role.Name == EmployeeRoles.Administrator).Id });
        await db.SaveChangesAsync();
    }

    private static SqlConnectionStringBuilder CreateIsolatedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
            ?? throw new InvalidOperationException("Set TEST_SQLSERVER_CONNECTION to an isolated SQL Server test server.");
        return new SqlConnectionStringBuilder(configured) { InitialCatalog = $"EstacionamentoEmployee_{Guid.NewGuid():N}" };
    }

    private static async Task DropDatabaseAsync(SqlConnectionStringBuilder connection)
    {
        await using var admin = new SqlConnection(new SqlConnectionStringBuilder(connection.ConnectionString) { InitialCatalog = "master" }.ConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"IF DB_ID(@databaseName) IS NOT NULL BEGIN ALTER DATABASE [{connection.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{connection.InitialCatalog}]; END";
        command.Parameters.AddWithValue("@databaseName", connection.InitialCatalog);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record TestActor(Guid? UserId, IReadOnlyCollection<string> Roles) : ICurrentActor;
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class FailingAuditWriter : IAuditWriter
    {
        public Task AppendAsync(Guid actorId, string action, Guid? targetId, DateTimeOffset occurredAtUtc,
            IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("synthetic audit failure");
    }

    private sealed class TestLogs : ILoggerProvider
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new TestLogger(Messages);
        public void Dispose() { }

        private sealed class TestLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, exception));
        }
    }
}
