using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

/// <summary>Configurable mail path for the five-day notice (and residual local Código). Activated when <see cref="MailOptions.Enabled"/>.</summary>
public sealed class SmtpMailSender(IOptions<MailOptions> options) : IMailSender
{
    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken)
    {
        var mail = options.Value;
        using var message = new MailMessage(mail.FromAddress, toEmail, subject, body);
        using var client = new SmtpClient(mail.Host, mail.Port)
        {
            EnableSsl = mail.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        if (!string.IsNullOrWhiteSpace(mail.UserName))
            client.Credentials = new NetworkCredential(mail.UserName, mail.Password);

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }
}
