using TransfersCheck.Core;

namespace TransfersCheck.Tests;

public sealed class RecordingMailSender : IMailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = [];

    public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken)
    {
        Sent.Add((toEmail, subject, body));
        return Task.CompletedTask;
    }
}
