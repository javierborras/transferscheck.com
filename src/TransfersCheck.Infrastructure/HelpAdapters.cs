using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

/// <summary>No-op when HelpCopilot:JevApiKey is empty. Does not invent credentials.</summary>
public sealed class HttpJevEvaluator(HttpClient http, IOptions<HelpCopilotOptions> options, ILogger<HttpJevEvaluator> logger) : IJevEvaluator
{
    public bool IsConfigured => options.Value.JevConfigured;

    public async Task<JevEvaluation?> EvaluateAsync(string stateJson, IReadOnlyList<JevQuestion> questions, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.JevBaseUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.JevApiKey);
            request.Content = JsonContent.Create(new
            {
                model = options.Value.JevModel,
                state = JsonSerializer.Deserialize<JsonElement>(stateJson),
                questions = questions.Select(question => new
                {
                    name = question.Name,
                    instructions = question.Instructions,
                    criteria = question.Criteria
                })
            });
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Jev returned {Status}; treating as unavailable.", (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var answers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("answers", out var answersNode))
            {
                foreach (var property in answersNode.EnumerateObject())
                    answers[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? ""
                        : property.Value.ToString();
            }
            return new JevEvaluation(answers);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Jev call failed; treating as unavailable.");
            return null;
        }
    }
}

/// <summary>Optional generative adapter. Empty GenerativeApiKey keeps generation off.</summary>
public sealed class HttpHelpSummarizer(HttpClient http, IOptions<HelpCopilotOptions> options, ILogger<HttpHelpSummarizer> logger) : IHelpSummarizer
{
    public bool IsConfigured => options.Value.GenerativeConfigured;

    public async Task<string?> SummarizeAsync(string question, HelpArticle article, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.GenerativeBaseUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.GenerativeApiKey);
            request.Content = JsonContent.Create(new
            {
                model = options.Value.GenerativeModel,
                provider = options.Value.GenerativeProvider,
                question,
                title = article.Title,
                summary = article.Summary,
                instruction = "Explain only what the help article supports. Keep it short. Do not invent links or product features."
            });
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Help summarizer returned {Status}.", (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (doc.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                return text.GetString();
            if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                return content.GetString();
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Help summarizer failed.");
            return null;
        }
    }
}

/// <summary>Reads <c>web/help/*.md</c> (YAML front matter) into drafts for publish (ADR-016).</summary>
public static class HelpCorpusFiles
{
    public static IReadOnlyList<HelpArticleDraft> ReadFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return [];

        var drafts = new List<HelpArticleDraft>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.md", SearchOption.TopDirectoryOnly).OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            var draft = Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
            if (draft is not null)
                drafts.Add(draft);
        }
        return drafts;
    }

    public static HelpArticleDraft? Parse(string markdown, string fallbackId)
    {
        var text = markdown.Replace("\r\n", "\n");
        if (!text.StartsWith("---\n", StringComparison.Ordinal))
            return null;
        var end = text.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0)
            return null;
        var front = text[4..end];
        var body = text[(end + 5)..].Trim();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in front.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            map[line[..colon].Trim()] = line[(colon + 1)..].Trim().Trim('"');
        }

        var id = map.GetValueOrDefault("id") ?? fallbackId;
        var title = map.GetValueOrDefault("title") ?? id;
        var summary = map.GetValueOrDefault("summary") ?? "";
        var publicPath = map.GetValueOrDefault("path") ?? $"/ayuda/{id}";
        var keywords = map.GetValueOrDefault("keywords") ?? "";
        if (string.IsNullOrWhiteSpace(summary))
            summary = body.Length <= 180 ? body : body[..180].Trim() + "…";
        return new HelpArticleDraft(id, title, summary, body, publicPath, keywords);
    }
}
