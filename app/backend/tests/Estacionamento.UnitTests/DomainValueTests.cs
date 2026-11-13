using Estacionamento.Domain.Billing;
using Estacionamento.Domain.Parking;
using Estacionamento.Application.Security;

namespace Estacionamento.UnitTests;

[TestClass]
public sealed class DomainValueTests
{
    [TestMethod]
    public void SpaceCode_NormalizesAndEnforcesLength()
    {
        Assert.AreEqual("A-12", SpaceCode.Create(" a-12 ").Value);
        Assert.ThrowsExactly<ArgumentException>(() => SpaceCode.Create("  "));
        Assert.ThrowsExactly<ArgumentException>(() => SpaceCode.Create(new string('A', SpaceCode.MaximumLength + 1)));
    }

    [TestMethod]
    public void VehiclePlate_NormalizesSeparatorsAndEnforcesLimit()
    {
        Assert.AreEqual("ABC1234", VehiclePlate.Create("abc-1234").Value);
        Assert.ThrowsExactly<ArgumentException>(() => VehiclePlate.Create("---"));
        Assert.ThrowsExactly<ArgumentException>(() => VehiclePlate.Create(new string('A', VehiclePlate.MaximumLength + 1)));
    }

    [TestMethod]
    public void Attendance_RequiresBoundedFieldsAndCreatesDistinctIdentifiers()
    {
        var first = Attendance.Create("abc123", "Model", "Owner", "document");
        var second = Attendance.Create("abc123", "Model", "Owner", "document");

        Assert.AreNotEqual(first.Id, second.Id);
        Assert.AreNotEqual(first.PublicIdentifier, second.PublicIdentifier);
        Assert.ThrowsExactly<ArgumentException>(() => Attendance.Create("abc123", " ", "Owner", "document"));
        Assert.ThrowsExactly<ArgumentException>(() => Attendance.Create("abc123", "Model", new string('O', Attendance.MaximumOwnerNameLength + 1), "document"));
        Assert.ThrowsExactly<ArgumentException>(() => Attendance.Create("abc123", "Model", "Owner", new string('D', Attendance.MaximumDocumentLength + 1)));
    }

    [TestMethod]
    public void DailyRate_RequiresPositiveValueWithCentPrecision()
    {
        Assert.AreEqual(10.25m, DailyRateAmount.Create(10.25m).Value);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DailyRateAmount.Create(0m));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DailyRateAmount.Create(1.001m));
    }

    [TestMethod]
    public void EntryInspection_RequiresTextAndActor()
    {
        Assert.AreEqual("checked", EntryInspection.Create(" checked ", Guid.NewGuid(), DateTimeOffset.UtcNow).Text);
        Assert.ThrowsExactly<ArgumentException>(() => EntryInspection.Create(" ", Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.ThrowsExactly<ArgumentException>(() => EntryInspection.Create(new string('x', EntryInspection.MaximumTextLength + 1), Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.ThrowsExactly<ArgumentException>(() => EntryInspection.Create("checked", Guid.Empty, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void Occupancy_RequiresIdentifiersAndClosesOnlyOnce()
    {
        var startedAt = DateTimeOffset.Parse("2026-11-13T10:00:00-03:00");
        var occupancy = Occupancy.Open(Guid.NewGuid(), Guid.NewGuid(), startedAt);

        Assert.IsTrue(occupancy.IsOpen);
        Assert.AreEqual(startedAt.ToUniversalTime(), occupancy.StartedAtUtc);
        Assert.IsFalse(occupancy.Close(startedAt.AddHours(1)).IsOpen);
        Assert.ThrowsExactly<ArgumentException>(() => Occupancy.Open(Guid.Empty, Guid.NewGuid(), startedAt));
        Assert.ThrowsExactly<InvalidOperationException>(() => occupancy.Close(startedAt.AddMinutes(-1)));
    }

    [TestMethod]
    public void UseCaseAuthorization_IsAppliedPerAction()
    {
        var authorizer = new RoleUseCaseAuthorizer();

        Assert.IsTrue(authorizer.IsAllowed([EmployeeRoles.Cashier], EmployeeAction.CreateAttendance));
        Assert.IsFalse(authorizer.IsAllowed([EmployeeRoles.Valet], EmployeeAction.CreateAttendance));
        Assert.IsFalse(authorizer.IsAllowed([EmployeeRoles.Valet], EmployeeAction.ReadFinancialDetails));
        Assert.IsTrue(authorizer.IsAllowed([EmployeeRoles.Valet], EmployeeAction.ReadEntryInspection));
        Assert.IsTrue(authorizer.IsAllowed([EmployeeRoles.Administrator], EmployeeAction.ManageEmployees));
        Assert.IsFalse(authorizer.IsAllowed([], EmployeeAction.ReadSpaces));
    }
}
