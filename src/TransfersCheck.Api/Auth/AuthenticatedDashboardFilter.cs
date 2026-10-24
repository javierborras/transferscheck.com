using Hangfire.Dashboard;

namespace TransfersCheck.Api.Auth;

/// <summary>
/// Development-only dashboard gate. Story 1.6 does not require a commerce session.
/// </summary>
public sealed class AuthenticatedDashboardFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
