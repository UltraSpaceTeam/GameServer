using GameServer.Services;
using Microsoft.Extensions.Configuration;

namespace GameServer.Tests.TestUtils;

public static class TokenServiceFactory
{
    public static TokenService Create()
    {
        var settings = new Dictionary<string, string?>
        {
            ["JwtSettings:SecretKey"] = "THIS_IS_A_TEST_SECRET_KEY_1234567890",
            ["JwtSettings:Issuer"] = "test-issuer",
            ["JwtSettings:Audience"] = "test-audience",
            ["JwtSettings:DurationInMinutes"] = "60"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new TokenService(config);
    }
}