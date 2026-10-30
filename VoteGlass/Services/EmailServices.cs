using Microsoft.EntityFrameworkCore;
using VoteGlass.Data;
using VoteGlass.Models;

namespace VoteGlass.Services;

public interface IAppEmailSender
{
    Task SendAsync(string to, string subject, string body);
}

// TEST MODE: nothing is really sent. The message is saved so the admin can read it on the Test inbox page.
public class TestEmailSender(IDbContextFactory<ApplicationDbContext> factory, ILogger<TestEmailSender> logger)
    : IAppEmailSender
{
    public async Task SendAsync(string to, string subject, string body)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.EmailMessages.Add(new EmailMessage { ToAddress = to, Subject = subject, Body = body });
        await db.SaveChangesAsync();
        logger.LogInformation("TEST EMAIL saved for {To}: {Subject}", to, subject);
    }
}