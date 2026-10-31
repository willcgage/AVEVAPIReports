using System.Security.Claims;

namespace PiReports.Web.Infrastructure;

public static class UserExtensions
{
    public static string DisplayName(this ClaimsPrincipal user) => user.Identity?.Name ?? "unknown";
}
