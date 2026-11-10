using System.Security.Claims;
using Dapper;

namespace ExpertSelfService.Services;

/// <summary>
/// Runs on every request that carries a login cookie (Program.cs, OnValidatePrincipal).
/// An expert who has been set to Retired or removed since logging in loses the login at once, on every page,
/// instead of keeping it for up to 60 minutes. Staff logins are not checked here.
/// </summary>
public sealed class ExpertSessionCheck(Database database)
{
    /// <summary>False when the login is an expert's and that expert is gone or Retired. Every status except Retired may stay (SPEC decision 9).</summary>
    public bool IsStillValid(ClaimsPrincipal user)
    {
        if (!user.HasClaim(claim => claim.Type == SessionClaims.ExpertId))
        {
            return true;
        }

        if (SessionClaims.ExpertIdOf(user) is not long expertId)
        {
            return false;
        }

        using var db = database.Open();
        return db.ExecuteScalar<long?>(
            "SELECT 1 FROM Candidates WHERE Id = @expertId AND Status <> @retired",
            new { expertId, retired = ExpertStatus.Retired }) is not null;
    }
}
