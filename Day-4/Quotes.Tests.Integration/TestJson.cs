using System.Text.Json;

namespace Quotes.Tests.Integration;

internal static class TestJson
{
    public static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web);
}
