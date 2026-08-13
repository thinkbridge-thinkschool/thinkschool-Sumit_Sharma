using System.Runtime.CompilerServices;

namespace Quotes.Tests.Integration;

internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void SetJwtSigningKey()
    {
        Environment.SetEnvironmentVariable(
            "Jwt__Key",
            "integration-test-signing-key-please-ignore-32b!");
    }
}
