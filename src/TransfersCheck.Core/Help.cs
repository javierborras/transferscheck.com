namespace TransfersCheck.Core;

/// <summary>Indexed help article (ADR-016 corpus). Links come from this metadata, never from a model.</summary>
public sealed class HelpArticle
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>Public SPA path, for example <c>/ayuda/conectar</c>.</summary>
    public string PublicPath { get; set; } = "";
    public string Keywords { get; set; } = "";
    public int CorpusVersion { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>Singleton row: published corpus version. A failed refresh leaves this alone (ADR-016).</summary>
public sealed class HelpCorpusState
{
    public int Id { get; set; } = 1;
    public int PublishedVersion { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed record HelpArticleDraft(
    string Id,
    string Title,
    string Summary,
    string Body,
    string PublicPath,
    string Keywords);

public sealed record HelpIngestResult(bool Published, int Version, string? Error);

public interface IHelpCorpus
{
    Task<int> GetPublishedVersionAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<HelpArticle>> ListPublishedAsync(CancellationToken cancellationToken);
    Task<HelpArticle?> FindPublishedAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<HelpArticle>> SearchPublishedAsync(string query, int take, CancellationToken cancellationToken);
    /// <summary>Writes a new version and flips published only after validation. Failure keeps the previous version.</summary>
    Task<HelpIngestResult> PublishAsync(IReadOnlyList<HelpArticleDraft> drafts, CancellationToken cancellationToken);
}

public sealed record HelpMenuOption(string Id, string Label, string Explanation, string? ArticleId);

/// <summary>Predetermined Pack Evento options. Answered locally with no external provider (ADR-017).</summary>
public static class HelpStaticMenu
{
    public static readonly IReadOnlyList<HelpMenuOption> Options =
    [
        new("conectar", "Cómo conectar Mercado Pago",
            "Abrí Configurar, seguí el checklist y autorizá la cuenta de cobro. El video de onboarding no es la ayuda; los pasos viven acá.",
            "conectar"),
        new("punto", "Qué ve un Punto de Alerta",
            "Un Punto de Alerta ve las últimas transferencias y puede abrir el detalle de una fila. No administra cuentas ni abre el historial completo.",
            "punto-alerta"),
        new("pagar", "Cómo pagar el Pack Mes",
            "En Pagos elegís el Pack Mes y seguís el link de Mercado Pago. El dinero va a la cuenta del dueño del producto.",
            "pagar"),
        new("actualizar", "Cuándo se actualizan las transferencias",
            "La lista se actualiza sola cada cinco minutos. También podés pedir una consulta con el botón de refresco; tiene un enfriamiento de un minuto.",
            "transferencias")
    ];

    public static HelpMenuOption? Find(string? optionId)
        => string.IsNullOrWhiteSpace(optionId)
            ? null
            : Options.FirstOrDefault(option => string.Equals(option.Id, optionId, StringComparison.OrdinalIgnoreCase));
}

public enum HelpFilterOutcome
{
    Allow,
    Block,
    Invalid
}

public static class HelpSecurityFilter
{
    private static readonly string[] ControlMarkers =
    [
        "ignore previous instructions",
        "ignore all previous",
        "system prompt",
        "you are now",
        "act as system",
        "<|",
        "[INST]",
        "```system"
    ];

    public static HelpFilterOutcome Evaluate(string? question, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(question))
            return HelpFilterOutcome.Invalid;
        var trimmed = question.Trim();
        if (trimmed.Length > maxLength)
            return HelpFilterOutcome.Invalid;
        var lower = trimmed.ToLowerInvariant();
        // A lone word such as "jailbreak" is not a block (ADR-017 / help-copilot-technical-spec).
        if (ControlMarkers.Any(marker => lower.Contains(marker, StringComparison.Ordinal)))
            return HelpFilterOutcome.Block;
        return HelpFilterOutcome.Allow;
    }
}

public sealed record HelpAskRequest(string? Question, string? OptionId);

public sealed record HelpAskResult(
    string Result,
    string? Explanation = null,
    string? Title = null,
    string? Link = null,
    string? Clarification = null);

public static class HelpAskResults
{
    public const string Answered = "answered";
    public const string Clarified = "clarify";
    public const string Blocked = "blocked";
    public const string Unavailable = "unavailable";
    public const string Refused = "refused";
}

public interface IHelpCopilot
{
    Task<HelpAskResult> AskAsync(bool enableCopilotHelp, HelpAskRequest request, CancellationToken cancellationToken);
}

/// <summary>TypeSafe Jev / System One. Empty API key keeps the adapter off — no fake secrets.</summary>
public interface IJevEvaluator
{
    bool IsConfigured { get; }
    Task<JevEvaluation?> EvaluateAsync(string stateJson, IReadOnlyList<JevQuestion> questions, CancellationToken cancellationToken);
}

public sealed record JevQuestion(string Name, string Instructions, string? Criteria = null);
public sealed record JevEvaluation(IReadOnlyDictionary<string, string> Answers);

/// <summary>Generative summary adapter. Empty config means no generation (ADR-017).</summary>
public interface IHelpSummarizer
{
    bool IsConfigured { get; }
    Task<string?> SummarizeAsync(string question, HelpArticle article, CancellationToken cancellationToken);
}
