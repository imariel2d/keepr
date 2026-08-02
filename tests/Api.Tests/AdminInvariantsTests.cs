using Keepr.Api.Domain;
using Keepr.Api.Features.Admin;

namespace Api.Tests;

/// <summary>
/// The two pure guards behind a role change, lifted out of <see cref="AdminController"/> so they can
/// be checked without a database: you can't demote yourself, and you can't remove the last admin. The
/// controller's surrounding I/O (loading the user, the advisory-lock-serialized admin count) is
/// covered end-to-end. See docs/feature-36-account-provisioning.md §5 and docs/testing-strategy.md.
/// </summary>
public class AdminInvariantsTests
{
    [Theory]
    [InlineData(true, Role.User, true)]    // demoting yourself — refused
    [InlineData(true, Role.Admin, false)]  // "changing" yourself to Admin — fine (no-op)
    [InlineData(false, Role.User, false)]  // demoting someone else — not self, allowed here
    [InlineData(false, Role.Admin, false)]
    public void IsSelfDemotion_only_trips_when_you_lower_your_own_role(
        bool isSelf, Role newRole, bool expected)
    {
        Assert.Equal(expected, AdminInvariants.IsSelfDemotion(isSelf, newRole));
    }

    [Theory]
    [InlineData(Role.Admin, Role.User, 0, true)]    // last admin standing — refused
    [InlineData(Role.Admin, Role.User, 1, false)]   // another admin remains — fine
    [InlineData(Role.Admin, Role.User, 5, false)]
    [InlineData(Role.Admin, Role.Admin, 0, false)]  // not a demotion
    [InlineData(Role.User, Role.User, 0, false)]    // target isn't an admin to begin with
    [InlineData(Role.User, Role.Admin, 0, false)]   // a promotion never removes an admin
    public void WouldRemoveLastAdmin_only_trips_on_demoting_the_sole_admin(
        Role current, Role next, int otherActiveAdmins, bool expected)
    {
        Assert.Equal(expected, AdminInvariants.WouldRemoveLastAdmin(current, next, otherActiveAdmins));
    }
}
