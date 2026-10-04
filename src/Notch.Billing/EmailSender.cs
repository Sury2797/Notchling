using System.Net;
using System.Net.Mail;

namespace Notch.Billing;

public interface ILoginEmailSender { Task SendAsync(string email, string code, CancellationToken token); }
public sealed class SmtpLoginEmailSender(BillingOptions options) : ILoginEmailSender
{
    public async Task SendAsync(string email, string code, CancellationToken token)
    {
        using var message = new MailMessage(options.MailFrom, email)
        { Subject = "Your Notch verification code", Body = $"Your Notch code is {code}. It expires in 10 minutes. If you did not request this, ignore this email." };
        using var client = new SmtpClient(options.SmtpHost, options.SmtpPort)
        { EnableSsl = true, Credentials = new NetworkCredential(options.SmtpUsername, options.SmtpPassword), Timeout = 15000 };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await client.SendMailAsync(message, deadline.Token);
    }
}
