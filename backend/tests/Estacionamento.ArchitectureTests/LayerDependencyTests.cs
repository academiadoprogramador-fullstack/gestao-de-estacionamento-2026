using System.Reflection;

namespace Estacionamento.ArchitectureTests;

[TestClass]
public sealed class LayerDependencyTests
{
    [TestMethod]
    public void Domain_does_not_reference_other_application_layers()
    {
        AssertDoesNotReference(
            typeof(Estacionamento.Domain.Class1).Assembly,
            "Estacionamento.Application",
            "Estacionamento.Infrastructure",
            "Estacionamento.Api");
    }

    [TestMethod]
    public void Application_does_not_reference_infrastructure_or_api()
    {
        AssertDoesNotReference(
            typeof(Estacionamento.Application.Class1).Assembly,
            "Estacionamento.Infrastructure",
            "Estacionamento.Api");
    }

    [TestMethod]
    public void Infrastructure_does_not_reference_api()
    {
        AssertDoesNotReference(
            typeof(Estacionamento.Infrastructure.Class1).Assembly,
            "Estacionamento.Api");
    }

    private static void AssertDoesNotReference(Assembly assembly, params string[] forbiddenPrefixes)
    {
        var forbiddenReferences = assembly.GetReferencedAssemblies()
            .Where(reference => forbiddenPrefixes.Any(prefix =>
                reference.Name?.StartsWith(prefix, StringComparison.Ordinal) == true))
            .Select(reference => reference.Name)
            .ToArray();

        Assert.AreEqual(0, forbiddenReferences.Length,
            $"{assembly.GetName().Name} has forbidden references: {string.Join(", ", forbiddenReferences)}");
    }
}
