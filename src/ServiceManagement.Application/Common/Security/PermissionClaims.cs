namespace ServiceManagement.Application.Common.Security;

/// <summary>
/// Claim types used by the permission-based authorization model. They live here
/// so the token issuer and the authorization handler can never drift apart.
/// </summary>
public static class PermissionClaims
{
    public const string Permission = "permission";

    public const string SecurityStamp = "security_stamp";
}
