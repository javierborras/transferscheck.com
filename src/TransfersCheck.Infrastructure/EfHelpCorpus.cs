using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class EfHelpCorpus(TransfersCheckDb db) : IHelpCorpus
{
    public async Task<int> GetPublishedVersionAsync(CancellationToken cancellationToken)
    {
        var state = await db.HelpCorpusStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == 1, cancellationToken);
        return state?.PublishedVersion ?? 0;
    }

    public async Task<IReadOnlyList<HelpArticle>> ListPublishedAsync(CancellationToken cancellationToken)
    {
        var version = await GetPublishedVersionAsync(cancellationToken);
        if (version <= 0)
            return [];
        return await db.HelpArticles.AsNoTracking()
            .Where(article => article.Active && article.CorpusVersion == version)
            .OrderBy(article => article.Title)
            .ToListAsync(cancellationToken);
    }

    public async Task<HelpArticle?> FindPublishedAsync(string id, CancellationToken cancellationToken)
    {
        var version = await GetPublishedVersionAsync(cancellationToken);
        if (version <= 0)
            return null;
        return await db.HelpArticles.AsNoTracking()
            .FirstOrDefaultAsync(article => article.Active && article.CorpusVersion == version && article.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<HelpArticle>> SearchPublishedAsync(string query, int take, CancellationToken cancellationToken)
    {
        var published = await ListPublishedAsync(cancellationToken);
        if (published.Count == 0 || string.IsNullOrWhiteSpace(query))
            return [];

        var tokens = query.ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n', ',', '.', '?', '!'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (tokens.Length == 0)
            return published.Take(take).ToList();

        return published
            .Select(article => (article, score: Score(article, tokens)))
            .Where(row => row.score > 0)
            .OrderByDescending(row => row.score)
            .ThenBy(row => row.article.Title)
            .Take(take)
            .Select(row => row.article)
            .ToList();
    }

    public async Task<HelpIngestResult> PublishAsync(IReadOnlyList<HelpArticleDraft> drafts, CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
            return new HelpIngestResult(false, 0, "empty_corpus");
        if (drafts.Any(draft => string.IsNullOrWhiteSpace(draft.Id) || string.IsNullOrWhiteSpace(draft.Title) || string.IsNullOrWhiteSpace(draft.PublicPath)))
            return new HelpIngestResult(false, 0, "invalid_article");
        if (drafts.Select(draft => draft.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != drafts.Count)
            return new HelpIngestResult(false, 0, "duplicate_id");

        var state = await db.HelpCorpusStates.SingleOrDefaultAsync(row => row.Id == 1, cancellationToken);
        if (state is null)
        {
            state = new HelpCorpusState { Id = 1, PublishedVersion = 0 };
            db.HelpCorpusStates.Add(state);
        }

        var next = state.PublishedVersion + 1;
        try
        {
            foreach (var draft in drafts)
            {
                db.HelpArticles.Add(new HelpArticle
                {
                    Id = draft.Id.Trim(),
                    Title = draft.Title.Trim(),
                    Summary = draft.Summary.Trim(),
                    Body = draft.Body.Trim(),
                    PublicPath = draft.PublicPath.Trim(),
                    Keywords = draft.Keywords.Trim(),
                    CorpusVersion = next,
                    Active = true
                });
            }

            state.PublishedVersion = next;
            state.PublishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return new HelpIngestResult(true, next, null);
        }
        catch (Exception exception)
        {
            db.ChangeTracker.Clear();
            return new HelpIngestResult(false, state.PublishedVersion, exception.GetType().Name);
        }
    }

    private static int Score(HelpArticle article, string[] tokens)
    {
        var haystack = $"{article.Id} {article.Title} {article.Keywords} {article.Summary} {article.Body}".ToLowerInvariant();
        var score = 0;
        foreach (var token in tokens)
        {
            if (article.Id.Equals(token, StringComparison.OrdinalIgnoreCase))
                score += 8;
            else if (article.Title.Contains(token, StringComparison.OrdinalIgnoreCase))
                score += 4;
            else if (article.Keywords.Contains(token, StringComparison.OrdinalIgnoreCase))
                score += 3;
            else if (haystack.Contains(token, StringComparison.Ordinal))
                score += 1;
        }
        return score;
    }
}
