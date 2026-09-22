# Authentication flow

## Login, rotation and reuse detection

```mermaid
sequenceDiagram
    autonumber
    participant C as Client
    participant A as API
    participant D as Database

    C->>A: POST /auth/login
    A->>D: load user by normalised email
    A->>A: verify PBKDF2 password hash
    alt invalid credentials or inactive account
        A->>D: audit LoginFailed
        A-->>C: 401 "Invalid e-mail or password."
    else valid
        A->>D: insert RefreshToken (SHA-256 hash, expiry, IP)
        A->>D: record LastLoginAt
        A->>D: audit Login
        A-->>C: access token + refresh token
    end

    C->>A: POST /auth/refresh
    A->>D: find refresh token by hash
    alt active
        A->>D: revoke old (reason Rotated, replacedBy = new hash)
        A->>D: insert rotated token
        A->>D: audit TokenRefreshed
        A-->>C: new access + refresh pair
    else revoked or expired (possible replay)
        A->>D: revoke ALL active tokens of that user (reason ReuseDetected)
        A-->>C: 401 - sign in again
    end

    C->>A: POST /auth/logout
    A->>D: revoke presented token (idempotent)
    A-->>C: 200
```

## Permission-based authorization

```mermaid
flowchart LR
    TOK["JWT access token<br/>claims: sub, roles, permission × N"] --> END["[RequirePermission('tickets.assign')]"]
    END --> PP{"PermissionPolicyProvider"}
    PP -->|"policy name starts with 'permission:'"| BUILD["build policy on demand"]
    PP -->|other policy| FALLBACK["DefaultAuthorizationPolicyProvider"]
    BUILD --> HAND["PermissionAuthorizationHandler"]
    HAND --> EVAL["PermissionEvaluator<br/>case-insensitive claim match"]
    EVAL -->|match| OK["authorized"]
    EVAL -->|no match| NO["403 in the standard envelope"]
```

## Token properties

| Property | Value |
| --- | --- |
| Access token | HMAC-SHA256 JWT, issuer/audience validated, 30-minute lifetime, 30-second clock skew |
| Refresh token | 64 random bytes, single use, 7-day lifetime |
| Stored form | `SHA-256(token)` hex — the raw token is never persisted |
| Rotation | Every refresh invalidates the presented token and links it to its successor |
| Reuse response | The whole active token family of that user is revoked |
| Password change / deactivation | Security stamp rotated (invalidates access tokens) + all refresh tokens revoked |
