using System.Diagnostics;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using QuotesApi.Auth;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Telemetry;

namespace Quotes.Tests.Unit;

public class AuthServiceRefreshTests
{
    [Fact]
    public async Task LoginAsync_WithUnknownEmail_ReturnsNull()
    {
        using var database = new TestDatabase();
        var sut = CreateAuthService(database.Db, Substitute.For<ILogger<AuthService>>());

        var result = await sut.LoginAsync(
            "nobody@example.com",
            "Whatever1!",
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsNull()
    {
        using var database = new TestDatabase();
        var sut = CreateAuthService(database.Db, Substitute.For<ILogger<AuthService>>());

        var user = new User
        {
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")
        };
        database.Db.Users.Add(user);
        await database.Db.SaveChangesAsync();

        var result = await sut.LoginAsync(
            user.Email,
            "WrongPassword!",
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_WithValidToken_ReturnsNewTokenPair()
    {
        var arranged = await ArrangeLoggedInUserAsync();
        using var database = arranged.Database;

        var result = await arranged.Sut.RefreshAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.RefreshToken.Should().NotBe(arranged.Login.RefreshToken);
        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.ExpiresIn.Should().Be(900);
    }

    [Fact]
    public async Task RefreshAsync_WithExpiredToken_ReturnsNull()
    {
        var arranged = await ArrangeLoggedInUserAsync();
        using var database = arranged.Database;
        var storedToken = await database.Db.RefreshTokens.SingleAsync();
        storedToken.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await database.Db.SaveChangesAsync();

        var result = await arranged.Sut.RefreshAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_WithRevokedToken_ReturnsNull()
    {
        var arranged = await ArrangeLoggedInUserAsync();
        using var database = arranged.Database;
        var storedToken = await database.Db.RefreshTokens.SingleAsync();
        storedToken.RevokedAt = DateTime.UtcNow;
        await database.Db.SaveChangesAsync();

        var result = await arranged.Sut.RefreshAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_WithReusedToken_ReturnsNull()
    {
        var arranged = await ArrangeLoggedInUserAsync();
        using var database = arranged.Database;
        var firstRefresh = await arranged.Sut.RefreshAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);
        firstRefresh.Should().NotBeNull();

        var reuseResult = await arranged.Sut.RefreshAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);

        reuseResult.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_WithValidToken_RevokesOldToken()
    {
        var arranged = await ArrangeLoggedInUserAsync();
        using var database = arranged.Database;

        await arranged.Sut.RefreshAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);

        var tokens = await database.Db.RefreshTokens.ToListAsync();
        tokens.Should().HaveCount(2);
        var oldToken = tokens.Single(token => token.ReplacedByToken != null);
        oldToken.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RefreshAsync_WithValidToken_CreatesReplacementTokenLinkedToOldToken()
    {
        var arranged = await ArrangeLoggedInUserAsync();
        using var database = arranged.Database;

        await arranged.Sut.RefreshAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);

        var tokens = await database.Db.RefreshTokens.ToListAsync();
        var oldToken = tokens.Single(token => token.RevokedAt != null);
        var newToken = tokens.Single(token => token.RevokedAt == null);
        oldToken.ReplacedByToken.Should().Be(newToken.Token);
        newToken.UserId.Should().Be(oldToken.UserId);
    }

    [Fact]
    public async Task RefreshAsync_WithReusedReplacedToken_RevokesReplacementTokenToo()
    {
        var arranged = await ArrangeLoggedInUserAsync();
        using var database = arranged.Database;
        await arranged.Sut.RefreshAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);

        var reuseResult = await arranged.Sut.RefreshAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);

        reuseResult.Should().BeNull();
        var tokens = await database.Db.RefreshTokens.ToListAsync();
        tokens.Should().HaveCount(2);
        tokens.Should().OnlyContain(token => token.RevokedAt != null);
    }

    [Fact]
    public async Task RefreshAsync_WithValidToken_RecordsCustomActivityWithUserIdAndRotatedResult()
    {
        var recordedActivities = new List<Activity>();
        using var listener = CreateQuotesApiActivityListener(recordedActivities);
        ActivitySource.AddActivityListener(listener);

        var arranged = await ArrangeLoggedInUserAsync();
        using var database = arranged.Database;

        var result = await arranged.Sut.RefreshAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);

        result.Should().NotBeNull();
        var activity = recordedActivities.Should()
            .ContainSingle(activity => activity.OperationName == "auth.refresh_token")
            .Subject;
        activity.GetTagItem("auth.refresh_result").Should().Be("rotated");
        activity.GetTagItem("user.id").Should().NotBeNull();
    }

    [Fact]
    public async Task RefreshAsync_WithReusedToken_RecordsCustomActivityWithReuseDetectedResult()
    {
        var recordedActivities = new List<Activity>();
        using var listener = CreateQuotesApiActivityListener(recordedActivities);
        ActivitySource.AddActivityListener(listener);

        var arranged = await ArrangeLoggedInUserAsync();
        using var database = arranged.Database;
        await arranged.Sut.RefreshAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);
        recordedActivities.Clear();

        var reuseResult = await arranged.Sut.RefreshAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);

        reuseResult.Should().BeNull();
        var activity = recordedActivities.Should()
            .ContainSingle(activity => activity.OperationName == "auth.refresh_token")
            .Subject;
        activity.GetTagItem("auth.refresh_result").Should().Be("reuse_detected");
    }

    [Fact]
    public async Task RefreshAsync_WithUnknownToken_ReturnsNull()
    {
        using var database = new TestDatabase();
        var sut = CreateAuthService(database.Db, Substitute.For<ILogger<AuthService>>());

        var result = await sut.RefreshAsync(
            "a-token-that-was-never-issued",
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_WhenUserNoLongerExists_ReturnsNull()
    {
        var arranged = await ArrangeLoggedInUserAsync();
        using var database = arranged.Database;
        var user = await database.Db.Users.SingleAsync();
        database.Db.Users.Remove(user);
        await database.Db.SaveChangesAsync();

        var result = await arranged.Sut.RefreshAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LogoutAsync_WithValidToken_RevokesToken()
    {
        var arranged = await ArrangeLoggedInUserAsync();
        using var database = arranged.Database;

        await arranged.Sut.LogoutAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);

        var storedToken = await database.Db.RefreshTokens.SingleAsync();
        storedToken.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task LogoutAsync_WithAlreadyRevokedToken_LeavesRevokedAtUnchanged()
    {
        var arranged = await ArrangeLoggedInUserAsync();
        using var database = arranged.Database;
        await arranged.Sut.LogoutAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);
        var revokedAtAfterFirstLogout = (await database.Db.RefreshTokens.SingleAsync()).RevokedAt;

        await arranged.Sut.LogoutAsync(
            arranged.Login.RefreshToken,
            CancellationToken.None);

        var storedToken = await database.Db.RefreshTokens.SingleAsync();
        storedToken.RevokedAt.Should().Be(revokedAtAfterFirstLogout);
    }

    [Fact]
    public async Task LogoutAsync_WithUnknownToken_DoesNotThrow()
    {
        using var database = new TestDatabase();
        var sut = CreateAuthService(database.Db, Substitute.For<ILogger<AuthService>>());

        var action = async () => await sut.LogoutAsync(
            "a-token-that-was-never-issued",
            CancellationToken.None);

        await action.Should().NotThrowAsync();
    }

    private static async Task<ArrangedAuthService> ArrangeLoggedInUserAsync()
    {
        var database = new TestDatabase();
        var logger = Substitute.For<ILogger<AuthService>>();
        var sut = CreateAuthService(database.Db, logger);

        var user = new User
        {
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")
        };
        database.Db.Users.Add(user);
        await database.Db.SaveChangesAsync();

        var login = await sut.LoginAsync(
            user.Email,
            "P@ssw0rd!",
            CancellationToken.None);

        if (login is null)
            throw new InvalidOperationException("Login failed during test arrangement.");

        return new ArrangedAuthService(database, sut, login);
    }

    private static ActivityListener CreateQuotesApiActivityListener(
        List<Activity> recordedActivities)
    {
        return new ActivityListener
        {
            ShouldListenTo = source => source.Name == QuotesApiActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activity => recordedActivities.Add(activity)
        };
    }

    private static AuthService CreateAuthService(
        AppDbContext db,
        ILogger<AuthService> logger)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "unit-test-signing-key-with-sufficient-length-000000",
                ["Jwt:Issuer"] = "QuotesApi.Tests",
                ["Jwt:Audience"] = "QuotesApi.Tests.Clients"
            })
            .Build();

        return new AuthService(db, configuration, logger);
    }

    private sealed record ArrangedAuthService(
        TestDatabase Database,
        AuthService Sut,
        TokenPair Login);

    private sealed class TestDatabase : IDisposable
    {
        private readonly SqliteConnection connection;

        public AppDbContext Db { get; }

        public TestDatabase()
        {
            connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            Db = new AppDbContext(options);
            Db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            Db.Dispose();
            connection.Dispose();
        }
    }
}
