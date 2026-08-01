namespace Keepr.Api.Features.Email;

/// <summary>
/// The default sender when no provider is configured (<c>Email:Provider</c> unset or <c>none</c>).
/// It sends nothing and logs that it dropped the message, so email stays optional. Its presence is
/// why the admin create path checks <see cref="EmailOptions.Enabled"/> before offering invite mode
/// (§4.2): an invite must never be silently no-op'd. See docs/feature-36-account-provisioning.md §6.1.
/// </summary>
public sealed class NoOpEmailSender(ILogger<NoOpEmailSender> log) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        // Deliberately no recipient address: it's PII (a personal identifier), and this fires on
        // every send in the documented default config, so logging it would scatter emails through
        // logs that are often shipped or retained. The subject alone is enough of a signal.
        log.LogInformation(
            "Email delivery is not configured; dropped a message (subject: {Subject}). "
            + "Set Email__Provider to enable outbound mail.", message.Subject);
        return Task.CompletedTask;
    }
}
