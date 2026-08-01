using Keepr.Api.Services;

namespace Keepr.Api.Features.Auth;

/// <summary>
/// Applies the email and password rules — <see cref="EmailPolicy"/>, <see cref="PasswordPolicy"/>,
/// and the breach check — and returns a per-field error map, or null when the credentials are
/// acceptable. Shared so self-registration, admin-provisioned accounts, invite claims, and
/// change-password all hold credentials to the same bar and report failures the same way.
///
/// Every failure is collected, not just the first, so a form can mark each bad field in one
/// round-trip. See docs/feature-3-registration-validation.md and feature-36-account-provisioning.md §4.3.
/// </summary>
public class CredentialValidator(IBreachedPasswordCheck breachCheck)
{
    /// <summary>Validates a full email + password pair (registration, admin direct-create).</summary>
    public async Task<Dictionary<string, string[]>?> ValidateAsync(
        string email, string? password, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        if (EmailPolicy.Validate(email) is { } emailError)
            errors["email"] = [emailError];

        AddPasswordErrors(errors, await PasswordFailuresAsync(password, email, ct));

        return errors.Count == 0 ? null : errors;
    }

    /// <summary>Validates a password alone against an account's email (invite claim, change-password),
    /// keyed under "password".</summary>
    public async Task<Dictionary<string, string[]>?> ValidatePasswordAsync(
        string? password, string email, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        AddPasswordErrors(errors, await PasswordFailuresAsync(password, email, ct));
        return errors.Count == 0 ? null : errors;
    }

    private async Task<List<string>> PasswordFailuresAsync(
        string? password, string email, CancellationToken ct)
    {
        var passwordErrors = PasswordPolicy.Validate(password, email);

        // Only worth a network round-trip once the password is otherwise acceptable — a password
        // already too short is going to be rejected either way.
        if (passwordErrors.Count == 0 && await breachCheck.IsBreachedAsync(password!, ct))
            passwordErrors.Add(PasswordPolicy.BreachedMessage);

        return passwordErrors;
    }

    private static void AddPasswordErrors(Dictionary<string, string[]> errors, List<string> failures)
    {
        if (failures.Count > 0) errors["password"] = [.. failures];
    }
}
