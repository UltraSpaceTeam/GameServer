using System.Security.Claims;
using FluentAssertions;
using GameServer.Controllers;
using GameServer.DTOs;
using GameServer.Tests.TestUtils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GameServer.Tests.Controllers;

public class AuthControllerTests
{
    [Fact]
    public async Task Register_WhenUsernameExists_ReturnsUnauthorized()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        Seed.AddPlayer(ctx, "egor", "pass");

        var tokenService = TokenServiceFactory.Create();
        var controller = new AuthController(ctx, tokenService);

        var result = await controller.Register(new LoginRequest("egor", "any"));

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Register_WhenValid_CreatesPlayerAndLeaderboard_AndReturnsAuthResponse()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var tokenService = TokenServiceFactory.Create();
        var controller = new AuthController(ctx, tokenService);

        var result = await controller.Register(new LoginRequest("new_user", "secret"));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<AuthResponse>();

        var response = (AuthResponse)ok.Value!;
        response.Username.Should().Be("new_user");
        response.PlayerId.Should().BeGreaterThan(0);
        response.Token.Should().NotBeNullOrWhiteSpace();

        var player = ctx.Players.Single(p => p.Username == "new_user");
        player.PasswordHash.Should().NotBeNullOrWhiteSpace();
        player.PasswordHash.Should().NotBe("secret");

        ctx.Leaderboard.Single(s => s.PlayerId == player.Id);
    }

    [Fact]
    public async Task Login_WhenUserNotFound_ReturnsUnauthorized()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var tokenService = TokenServiceFactory.Create();
        var controller = new AuthController(ctx, tokenService);

        var result = await controller.Login(new LoginRequest("missing", "pass"));

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Login_WhenPasswordWrong_ReturnsUnauthorized()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        Seed.AddPlayer(ctx, "egor", "correct");

        var tokenService = TokenServiceFactory.Create();
        var controller = new AuthController(ctx, tokenService);

        var result = await controller.Login(new LoginRequest("egor", "wrong"));

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Login_WhenValid_ReturnsOkAuthResponse()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var player = Seed.AddPlayer(ctx, "egor", "pass");

        var tokenService = TokenServiceFactory.Create();
        var controller = new AuthController(ctx, tokenService);

        var result = await controller.Login(new LoginRequest("egor", "pass"));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<AuthResponse>();

        var response = (AuthResponse)ok.Value!;
        response.PlayerId.Should().Be(player.Id);
        response.Username.Should().Be("egor");
        response.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task VerifyToken_WhenNoNameIdentifierClaim_ReturnsUnauthorized()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var tokenService = TokenServiceFactory.Create();
        var controller = new AuthController(ctx, tokenService);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            }
        };

        var result = await controller.VerifyToken();

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task VerifyToken_WhenUserDoesNotExist_ReturnsUnauthorized()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var tokenService = TokenServiceFactory.Create();
        var controller = new AuthController(ctx, tokenService);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "99999") }, "test"))
            }
        };

        var result = await controller.VerifyToken();

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task VerifyToken_WhenValid_ReturnsOkWithPlayerData()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var player = Seed.AddPlayer(ctx, "egor", "pass");

        var tokenService = TokenServiceFactory.Create();
        var controller = new AuthController(ctx, tokenService);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, player.Id.ToString()) }, "test"))
            }
        };

        var result = await controller.VerifyToken();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;

        var payload = ok.Value!;
        payload.Should().NotBeNull();

        payload.GetType().GetProperty("valid")!.GetValue(payload).Should().Be(true);
        payload.GetType().GetProperty("player_id")!.GetValue(payload).Should().Be(player.Id);
        payload.GetType().GetProperty("username")!.GetValue(payload).Should().Be("egor");
    }
}