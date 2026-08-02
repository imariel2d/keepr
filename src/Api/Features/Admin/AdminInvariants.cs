using Keepr.Api.Domain;

namespace Keepr.Api.Features.Admin;

/// <summary>
/// The pure decision rules behind a role change, lifted out of <see cref="AdminController.UpdateRole"/>
/// so they can be unit-tested without a database. The controller still owns the surrounding I/O — it
/// loads the user, serializes the admin-count check on an advisory lock, and maps these verdicts to
/// the HTTP responses — but the "may this change happen?" logic lives here. See
/// docs/feature-36-account-provisioning.md §5 and docs/testing-strategy.md.
/// </summary>
public static class AdminInvariants
{
    /// <summary>
    /// An admin cannot demote their own account — setting yourself to anything other than
    /// <see cref="Role.Admin"/> is refused, so you can't lock yourself out of the admin surface.
    /// </summary>
    public static bool IsSelfDemotion(bool isSelf, Role newRole) => isSelf && newRole != Role.Admin;

    /// <summary>
    /// A demotion must not strand the instance with zero admins: demoting the last remaining admin
    /// (Admin → User with no other active admins) is refused. <paramref name="otherActiveAdmins"/> is
    /// the count of admins other than the target that are not deletion-pending.
    /// </summary>
    public static bool WouldRemoveLastAdmin(Role current, Role next, int otherActiveAdmins) =>
        current == Role.Admin && next == Role.User && otherActiveAdmins == 0;
}
