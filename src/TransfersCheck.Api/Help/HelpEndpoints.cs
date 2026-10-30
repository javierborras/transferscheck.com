using TransfersCheck.Api.Auth;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

namespace TransfersCheck.Api.Help;

/// <summary>Help center and copilot (Epic 11 / ADR-016 / ADR-017). No chatbot on the connect screen.</summary>
public static class HelpEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/help").AddEndpointFilter<LiveSessionFilter>();
        group.MapGet("/", Catalog);
        group.MapGet("/{id}", Article);
        group.MapPost("/ask", Ask);
        group.MapPost("/messages", CreateMessage);
    }

    private static async Task<IResult> Catalog(
        HttpContext http,
        IMerchantStore store,
        IPlanCatalog plans,
        IHelpCorpus corpus,
        CancellationToken cancellationToken)
    {
        var enableCopilot = await CopilotEnabledAsync(http, store, plans, cancellationToken);
        var articles = await corpus.ListPublishedAsync(cancellationToken);
        return Results.Ok(new
        {
            enableCopilotHelp = enableCopilot,
            helpCenterPath = "/ayuda",
            menu = HelpStaticMenu.Options.Select(option => new { id = option.Id, label = option.Label }),
            articles = articles.Select(article => new
            {
                id = article.Id,
                title = article.Title,
                summary = article.Summary,
                path = article.PublicPath
            }),
            corpusVersion = await corpus.GetPublishedVersionAsync(cancellationToken)
        });
    }

    private static async Task<IResult> Article(string id, IHelpCorpus corpus, CancellationToken cancellationToken)
    {
        var article = await corpus.FindPublishedAsync(id, cancellationToken);
        if (article is null)
            return Results.NotFound();
        return Results.Ok(new
        {
            id = article.Id,
            title = article.Title,
            summary = article.Summary,
            body = article.Body,
            path = article.PublicPath
        });
    }

    private sealed record AskBody(string? Question, string? OptionId);

    private sealed record HelpMessageBody(string? Text);

    private static async Task<IResult> CreateMessage(
        HelpMessageBody body,
        HttpContext http,
        TransfersCheckDb db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var text = body.Text?.Trim() ?? "";
        if (text.Length == 0)
            return Results.BadRequest(new { error = "text_required" });
        if (text.Length > HelpdeskStore.TextMaxLength)
            return Results.BadRequest(new { error = "text_too_long" });

        var account = (LoginAccount)http.Items["account"]!;
        var id = await HelpdeskStore.CreateMessageAsync(db, account.TenantId, text, clock.GetUtcNow(), cancellationToken);
        return Results.Json(new { id }, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> Ask(
        AskBody body,
        HttpContext http,
        IMerchantStore store,
        IPlanCatalog plans,
        IHelpCopilot copilot,
        CancellationToken cancellationToken)
    {
        var enableCopilot = await CopilotEnabledAsync(http, store, plans, cancellationToken);
        var result = await copilot.AskAsync(enableCopilot, new HelpAskRequest(body.Question, body.OptionId), cancellationToken);
        return Results.Ok(new
        {
            result = result.Result,
            explanation = result.Explanation,
            title = result.Title,
            link = result.Link,
            clarification = result.Clarification,
            enableCopilotHelp = enableCopilot
        });
    }

    private static async Task<bool> CopilotEnabledAsync(
        HttpContext http,
        IMerchantStore store,
        IPlanCatalog plans,
        CancellationToken cancellationToken)
    {
        var account = (LoginAccount)http.Items["account"]!;
        var merchant = await store.FindMerchantAsync(account.TenantId, cancellationToken);
        if (merchant?.PlanCode is not { } code)
            return false;
        var plan = await plans.FindByCodeAsync(code, cancellationToken);
        return plan?.EnableCopilotHelp == true;
    }
}
