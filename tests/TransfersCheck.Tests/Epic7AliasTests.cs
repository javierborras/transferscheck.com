using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class Epic7AliasTests(SqlFixture sql)
{
    [Fact]
    public async Task Sign_in_requires_the_email_code_and_refuses_it_after_one_hour()
    {
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock };
        using var client = factory.CreateClient();
        var email = $"code-{Guid.NewGuid():N}@counter.test";
        using var signup = await client.PostAsJsonAsync("/api/accounts", new { email, password = "password1" });
        signup.EnsureSuccessStatusCode();

        using var withoutCode = await client.PostAsJsonAsync("/api/sessions", new { email, password = "password1" });
        Assert.Equal(HttpStatusCode.Unauthorized, withoutCode.StatusCode);
        Assert.Equal("code_required", (await withoutCode.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Single(factory.Mail.Sent);
        var code = factory.Mail.Sent[^1].Body.Trim();
        Assert.Equal(6, code.Length);
        Assert.True(code.All(char.IsDigit));

        using var withCode = await client.PostAsJsonAsync("/api/sessions", new { email, password = "password1", code });
        Assert.Equal(HttpStatusCode.OK, withCode.StatusCode);

        using var again = await client.PostAsJsonAsync("/api/sessions", new { email, password = "password1" });
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        var lateCode = factory.Mail.Sent[^1].Body.Trim();
        clock.Now = clock.Now.AddHours(1).AddSeconds(1);
        using var expired = await client.PostAsJsonAsync("/api/sessions", new { email, password = "password1", code = lateCode });
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal("invalid_code", (await expired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var app = File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "App.tsx"));
        Assert.Contains("name=\"code\"", app, StringComparison.Ordinal);
        Assert.DoesNotContain("6 dígitos", app, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("six digits", app, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TransfersCheck.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
