using Microsoft.Extensions.Options;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

/// <summary>
/// Orchestrates help: static menu locally, free questions only when <c>EnableCopilotHelp</c> is on.
/// Strong corpus hits return the editorial summary without calling Jev or a generative provider.
/// Unevaluated generation is never emitted when adapters are off (ADR-017).
/// </summary>
public sealed class HelpCopilotService(
    IHelpCorpus corpus,
    IJevEvaluator jev,
    IHelpSummarizer summarizer,
    IOptions<HelpCopilotOptions> options) : IHelpCopilot
{
    public async Task<HelpAskResult> AskAsync(bool enableCopilotHelp, HelpAskRequest request, CancellationToken cancellationToken)
    {
        var helpCenter = options.Value.HelpCenterPath;

        if (!string.IsNullOrWhiteSpace(request.OptionId))
            return await AnswerOptionAsync(request.OptionId!, helpCenter, cancellationToken);

        if (!enableCopilotHelp)
            return new HelpAskResult(HelpAskResults.Refused,
                Clarification: "Tu plan responde con el menú de ayuda. Elegí una opción o pasá a un plan con copilot.");

        var filter = HelpSecurityFilter.Evaluate(request.Question, options.Value.MaxQuestionLength);
        if (filter == HelpFilterOutcome.Invalid)
            return new HelpAskResult(HelpAskResults.Clarified,
                Clarification: "Escribí una pregunta breve sobre cómo usar TransfersCheck.");
        if (filter == HelpFilterOutcome.Block)
            return new HelpAskResult(HelpAskResults.Blocked,
                Clarification: "Solo puedo orientar sobre el uso del producto. Abrí el centro de ayuda.",
                Link: helpCenter);

        var question = request.Question!.Trim();
        var candidates = await corpus.SearchPublishedAsync(question, 5, cancellationToken);
        if (candidates.Count == 0)
            return new HelpAskResult(HelpAskResults.Clarified,
                Clarification: "No encontré un artículo claro para eso. Reformulá o abrí el centro de ayuda.",
                Link: helpCenter);

        var best = candidates[0];
        if (IsStrongMatch(question, best))
            return new HelpAskResult(HelpAskResults.Answered, best.Summary, best.Title, best.PublicPath);

        if (!jev.IsConfigured)
            return new HelpAskResult(HelpAskResults.Unavailable,
                Clarification: "El asistente no está disponible ahora. Podés seguir por el centro de ayuda.",
                Link: helpCenter);

        var evaluation = await jev.EvaluateAsync(
            $$"""{"question":{{JsonSafe(question)}},"articleId":{{JsonSafe(best.Id)}},"summary":{{JsonSafe(best.Summary)}}}""",
            [new JevQuestion("scope", "Is this a product-help question about TransfersCheck usage?", "yes if in scope")],
            cancellationToken);
        if (evaluation is null)
            return new HelpAskResult(HelpAskResults.Unavailable,
                Clarification: "El asistente no está disponible ahora. Podés seguir por el centro de ayuda.",
                Link: helpCenter);

        if (evaluation.Answers.TryGetValue("scope", out var scope)
            && scope.Contains("no", StringComparison.OrdinalIgnoreCase))
            return new HelpAskResult(HelpAskResults.Clarified,
                Clarification: "Puedo ayudar con el uso de TransfersCheck. Probá otra pregunta o abrí el centro de ayuda.",
                Link: helpCenter);

        if (summarizer.IsConfigured)
        {
            var generated = await summarizer.SummarizeAsync(question, best, cancellationToken);
            if (!string.IsNullOrWhiteSpace(generated) && generated.Length <= 600)
                return new HelpAskResult(HelpAskResults.Answered, generated.Trim(), best.Title, best.PublicPath);
        }

        return new HelpAskResult(HelpAskResults.Answered, best.Summary, best.Title, best.PublicPath);
    }

    private async Task<HelpAskResult> AnswerOptionAsync(string optionId, string helpCenter, CancellationToken cancellationToken)
    {
        var option = HelpStaticMenu.Find(optionId);
        if (option is null)
            return new HelpAskResult(HelpAskResults.Clarified,
                Clarification: "Esa opción no está en el menú. Elegí otra o abrí el centro de ayuda.",
                Link: helpCenter);

        string? title = null;
        string? link = null;
        if (option.ArticleId is { } articleId)
        {
            var article = await corpus.FindPublishedAsync(articleId, cancellationToken);
            if (article is not null)
            {
                title = article.Title;
                link = article.PublicPath;
            }
        }

        return new HelpAskResult(HelpAskResults.Answered, option.Explanation, title, link ?? helpCenter);
    }

    private static bool IsStrongMatch(string question, HelpArticle article)
    {
        var tokens = Tokenize(question);
        if (tokens.Count == 0)
            return false;
        var haystack = $"{article.Id} {article.Title} {article.Keywords} {article.Summary}".ToLowerInvariant();
        var hits = tokens.Count(token => haystack.Contains(token, StringComparison.Ordinal));
        return hits >= Math.Max(1, (tokens.Count + 1) / 2);
    }

    private static List<string> Tokenize(string text)
        => text.ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n', ',', '.', '?', '!', ';', ':', '/', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string JsonSafe(string value)
        => System.Text.Json.JsonSerializer.Serialize(value);
}
