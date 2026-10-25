using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TransfersCheck.Core;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class Epic12PlanEditorTests(SqlFixture sql)
{
    [Fact]
    public async Task Owner_can_edit_plan_price_and_commerce_pay_screen_reads_active_plans()
    {
        await using var factory = new TransfersApiFactory(sql) { Owner = true };
        using var ownerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var signIn = await ownerClient.PostAsJsonAsync("/api/owner/sessions", new { email = "owner@transferscheck.test", password = "owner-password-1" });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        var ownerCookie = signIn.Headers.GetValues("Set-Cookie").Single(header => header.StartsWith("tc_owner=", StringComparison.Ordinal)).Split(';')[0];

        using var listed = await Send(ownerClient, "/api/owner/plans", ownerCookie);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var plans = await listed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(plans.EnumerateArray(), plan => plan.GetProperty("code").GetString() == PlanCodes.PackMes);

        using var updated = await Send(ownerClient, $"/api/owner/plans/{PlanCodes.PackMes}", ownerCookie, HttpMethod.Put, new
        {
            displayName = "Pack Mes",
            amount = 6100,
            currency = "ARS",
            durationDays = 30,
            faceCopy = "",
            enableCopilotHelp = true,
            active = true
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(6100, (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("amount").GetInt32());

        using var created = await Send(ownerClient, "/api/owner/plans", ownerCookie, HttpMethod.Post, new
        {
            code = "pack-extra",
            displayName = "Pack Extra",
            amount = 2500,
            currency = "ARS",
            durationDays = 7,
            faceCopy = "Una semana extra.",
            enableCopilotHelp = false,
            active = true
        });
        Assert.Equal(HttpStatusCode.Conflict, created.StatusCode);

        using var commerce = factory.CreateClient();
        var email = $"plan-edit-{Guid.NewGuid():N}@counter.test";
        using var signup = await commerce.PostAsJsonAsync("/api/accounts", new { email, password = "password1" });
        var commerceCookie = signup.Headers.GetValues("Set-Cookie").Single(header => header.StartsWith("tc_session=", StringComparison.Ordinal)).Split(';')[0];

        using var billing = await Send(commerce, "/api/billing", commerceCookie);
        Assert.Equal(HttpStatusCode.OK, billing.StatusCode);
        var body = await billing.Content.ReadFromJsonAsync<JsonElement>();
        var payPlans = body.GetProperty("plans").EnumerateArray().ToArray();
        Assert.Contains(payPlans, plan => plan.GetProperty("code").GetString() == PlanCodes.PackMes && plan.GetProperty("amount").GetInt32() == 6100);
        Assert.DoesNotContain(payPlans, plan => plan.GetProperty("code").GetString() == "pack-extra");

        var app = File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "App.tsx"));
        Assert.Contains("Planes", app, StringComparison.Ordinal);
        Assert.Contains("/api/owner/plans", app, StringComparison.Ordinal);
        Assert.DoesNotContain("plan-editing", app, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string url, string cookie, HttpMethod? method = null, object? json = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (json is not null)
            request.Content = JsonContent.Create(json);
        return await client.SendAsync(request);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TransfersCheck.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
