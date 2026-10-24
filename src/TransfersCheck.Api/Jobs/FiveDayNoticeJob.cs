using TransfersCheck.Core;

namespace TransfersCheck.Api.Jobs;

public sealed class FiveDayNoticeJob(IFiveDayNoticeStore store, IMailSender mail, TimeProvider clock)
{
    public async Task Execute()
    {
        var now = clock.GetUtcNow();
        foreach (var candidate in await store.ListCandidatesAsync(CancellationToken.None))
        {
            if (!FiveDayNotice.ShouldSend(candidate, now))
                continue;
            await mail.SendAsync(
                candidate.OwnerEmail,
                "TransfersCheck — quedan cinco días de acceso",
                "Tu acceso a TransfersCheck termina en cinco días. Pagá para seguir recibiendo avisos.",
                CancellationToken.None);
            await store.MarkSentAsync(candidate.TenantId, now, CancellationToken.None);
        }
    }
}
