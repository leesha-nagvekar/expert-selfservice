using System.Security.Claims;

namespace ExpertSelfService.Services;

/// <summary>
/// The login cookie holds exactly one of these claims: expert_id or staff_id, never both (SPEC section 2).
/// Expert pages use the "Expert" policy, secretariat pages the "Staff" policy.
/// </summary>
public static class SessionClaims
{
    public const string ExpertId = "expert_id";
    public const string StaffId = "staff_id";

    public const string ExpertPolicy = "Expert";
    public const string StaffPolicy = "Staff";

    public const string ExpertLanding = "/profile";
    public const string StaffLanding = "/staff";

    public static Claim For(Person person) =>
        new(person.Kind == PersonKind.Expert ? ExpertId : StaffId, person.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static string LandingFor(Person person) =>
        person.Kind == PersonKind.Expert ? ExpertLanding : StaffLanding;

    /// <summary>The expert id from the session, or null when the user isn't an expert. The only source of it.</summary>
    public static long? ExpertIdOf(ClaimsPrincipal user) =>
        long.TryParse(user.FindFirst(ExpertId)?.Value, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : null;

    /// <summary>The landing page of whoever is logged in, or null when nobody is.</summary>
    public static string? Landing(ClaimsPrincipal user) =>
        user.HasClaim(claim => claim.Type == ExpertId) ? ExpertLanding
        : user.HasClaim(claim => claim.Type == StaffId) ? StaffLanding
        : null;
}
