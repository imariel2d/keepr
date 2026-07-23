using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Auth;

/// <summary>Whether a registration attempt may proceed, and why not when it may not.</summary>
public readonly record struct GateDecision(bool Allowed, string? Reason, int StatusCode)
{
    public static GateDecision Allow() => new(true, null, StatusCodes.Status200OK);

    public static GateDecision Deny(string reason, int statusCode = StatusCodes.Status403Forbidden)
        => new(false, reason, statusCode);
}

/// <summary>
/// What a gate is allowed to look at. Widen this as new gate implementations need more context
/// (an emailed invite token would arrive here the same way the shared code does).
/// </summary>
public readonly record struct RegistrationAttempt(string Email, string? Secret);

/// <summary>
/// Decides who may create an account. Keepr is a small private deployment, so "can reach the
/// site" must not imply "can sign up".
///
/// This is deliberately an interface with one narrow method: swapping the shared code for
/// per-address emailed invites, an allow-list, or an admin approval queue means registering a
/// different implementation in Program.cs — <see cref="AuthController"/> does not change.
/// </summary>
public interface IRegistrationGate
{
    Task<GateDecision> EvaluateAsync(RegistrationAttempt attempt, CancellationToken ct);
}

public class RegistrationOptions
{
    public const string SectionName = "Registration";

    /// <summary>
    /// The shared invite code. Empty closes registration entirely — see
    /// <see cref="InviteCodeRegistrationGate"/> for why that is the safe default.
    /// </summary>
    public string InviteCode { get; set; } = "";
}

/// <summary>
/// A single shared invite code, rotated by changing configuration.
///
/// Fails closed: if no code is configured the gate refuses everyone rather than letting everyone
/// through, so a deployment that forgets to set <c>Registration__InviteCode</c> ends up with
/// signups disabled instead of silently open to the internet.
/// </summary>
public class InviteCodeRegistrationGate(
    IOptions<RegistrationOptions> options,
    ILogger<InviteCodeRegistrationGate> log) : IRegistrationGate
{
    // The gate is scoped (one per request), so the "no code configured" warning would otherwise
    // fire on every public registration attempt and flood the logs. IOptions is a startup snapshot
    // that never reloads, so warning once per process says everything an operator needs. 0 = not
    // yet warned; Interlocked makes the first-caller check atomic across concurrent requests.
    private static int _missingCodeWarned;

    public Task<GateDecision> EvaluateAsync(RegistrationAttempt attempt, CancellationToken ct)
    {
        var expected = options.Value.InviteCode?.Trim();

        if (string.IsNullOrWhiteSpace(expected))
        {
            if (Interlocked.Exchange(ref _missingCodeWarned, 1) == 0)
                log.LogWarning(
                    "Registration refused: no Registration:InviteCode is configured, so signups are closed.");
            return Task.FromResult(GateDecision.Deny("Registration is currently closed."));
        }

        var supplied = attempt.Secret?.Trim();
        if (string.IsNullOrWhiteSpace(supplied))
            return Task.FromResult(
                GateDecision.Deny("An invite code is required to create an account."));

        return Task.FromResult(Matches(expected, supplied)
            ? GateDecision.Allow()
            : GateDecision.Deny("That invite code is not valid."));
    }

    /// <summary>
    /// Compares fixed-size digests rather than the raw strings: FixedTimeEquals only accepts equal
    /// lengths, and hashing first stops the code's length leaking through the comparison.
    /// </summary>
    private static bool Matches(string expected, string supplied)
        => CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
}
