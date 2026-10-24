using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

/// <summary>Unbound production mail sender until ADR-012 chooses a provider.</summary>
public sealed class NullMailSender : IMailSender
{
    public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
