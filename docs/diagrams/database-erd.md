# Database ERD

```mermaid
erDiagram
    Users ||--o{ UserRoles : "has"
    Roles ||--o{ UserRoles : "assigned to"
    Roles ||--o{ RolePermissions : "includes"
    Permissions ||--o{ RolePermissions : "granted by"
    Users ||--o{ RefreshTokens : "owns"
    Users ||--o{ Notifications : "receives"

    Organizations ||--o{ Departments : "contains"
    Organizations ||--o{ Teams : "contains"
    Departments ||--o{ Teams : "contains"
    Teams ||--o{ TeamMembers : "contains"
    Users ||--o{ TeamMembers : "member of"
    Users ||--o| Teams : "manages"

    ServiceCategories ||--o{ ServiceCategories : "parent of"
    ServiceCategories ||--o{ Services : "classifies"
    ServicePriorities ||--o{ SlaPolicies : "targeted by"
    ServicePriorities ||--o{ Services : "default for"

    Services ||--o{ Tickets : "raised against"
    ServiceCategories ||--o{ Tickets : "classifies"
    ServicePriorities ||--o{ Tickets : "prioritises"
    Users ||--o{ Tickets : "requests"
    Users ||--o{ Tickets : "assigned to"
    Teams ||--o{ Tickets : "queued for"

    Tickets ||--o{ TicketComments : "has"
    Tickets ||--o{ TicketAssignmentHistory : "has"
    Users ||--o{ TicketComments : "authors"
    Users ||--o{ TicketAssignmentHistory : "changed by"

    Users {
        uniqueidentifier Id PK
        string Email UK
        string UserName UK
        string PasswordHash
        string SecurityStamp
        bool IsActive
        uniqueidentifier OrganizationId FK
        uniqueidentifier DepartmentId FK
        datetimeoffset CreatedAt
        datetimeoffset LastLoginAt
    }
    Roles {
        uniqueidentifier Id PK
        string Name
        string NormalizedName UK
        string Description
        bool IsSystemRole
    }
    Permissions {
        uniqueidentifier Id PK
        string Name
        string NormalizedName UK
        string Group
    }
    RefreshTokens {
        uniqueidentifier Id PK
        uniqueidentifier UserId FK
        string TokenHash UK
        datetimeoffset ExpiresAt
        datetimeoffset RevokedAt
        string ReplacedByTokenHash
        string ReasonRevoked
    }
    Organizations {
        uniqueidentifier Id PK
        string Name
        string Code UK
        bool IsActive
    }
    Departments {
        uniqueidentifier Id PK
        uniqueidentifier OrganizationId FK
        string Name
        string Code
    }
    Teams {
        uniqueidentifier Id PK
        uniqueidentifier OrganizationId FK
        uniqueidentifier DepartmentId FK
        uniqueidentifier ManagerId FK
        string Name
        string Code
    }
    ServiceCategories {
        uniqueidentifier Id PK
        uniqueidentifier ParentId FK
        string Name
        string Code UK
        bool IsActive
    }
    ServicePriorities {
        uniqueidentifier Id PK
        string Name
        string Code UK
        int Level
        string ColorHex
    }
    SlaPolicies {
        uniqueidentifier Id PK
        uniqueidentifier PriorityId FK
        string Name
        int FirstResponseMinutes
        int ResolutionMinutes
        int AtRiskThresholdPercent
        bool IsActive
    }
    Services {
        uniqueidentifier Id PK
        uniqueidentifier CategoryId FK
        uniqueidentifier DefaultPriorityId FK
        uniqueidentifier DefaultSlaPolicyId FK
        string Name
        string Code UK
        bool IsActive
    }
    Tickets {
        uniqueidentifier Id PK
        string TicketNumber UK
        string Title
        string Description
        uniqueidentifier ServiceId FK
        uniqueidentifier CategoryId FK
        uniqueidentifier PriorityId FK
        uniqueidentifier RequesterId FK
        uniqueidentifier AssignedToId FK
        uniqueidentifier AssignedTeamId FK
        string Status
        string SlaStatus
        datetimeoffset ReportedAt
        datetimeoffset FirstResponseDueAt
        datetimeoffset ResolutionDueAt
        datetimeoffset ResolvedAt
        datetimeoffset ClosedAt
        byte[] RowVersion
        string IdempotencyKey
        bool IsDeleted
    }
    TicketComments {
        uniqueidentifier Id PK
        uniqueidentifier TicketId FK
        uniqueidentifier AuthorId FK
        string Content
        bool IsInternal
    }
    TicketAssignmentHistory {
        uniqueidentifier Id PK
        uniqueidentifier TicketId FK
        uniqueidentifier PreviousAssigneeId FK
        uniqueidentifier NewAssigneeId FK
        uniqueidentifier PreviousTeamId FK
        uniqueidentifier NewTeamId FK
        uniqueidentifier ChangedBy FK
        string Reason
        datetimeoffset ChangedAt
    }
    Notifications {
        uniqueidentifier Id PK
        uniqueidentifier UserId FK
        string Type
        string Title
        string Message
        string EntityName
        string EntityId
        bool IsRead
        datetimeoffset ReadAt
    }
    AuditLogs {
        uniqueidentifier Id PK
        uniqueidentifier UserId
        string Action
        string EntityName
        string EntityId
        datetimeoffset OccurredAt
        string IpAddress
        string CorrelationId
        string OldValues
        string NewValues
    }
```

## Index highlights

| Table | Index | Purpose |
| --- | --- | --- |
| `Tickets` | `IX_Tickets_TicketNumber` (unique) | Number lookup |
| `Tickets` | `IX_Tickets_IdempotencyKey` (unique, filtered `IS NOT NULL`) | Idempotent creation |
| `Tickets` | `Status`, `SlaStatus`, `ReportedAt`, `ResolutionDueAt` | Queue views, SLA monitor, default sort |
| `Tickets` | `RequesterId`, `AssignedToId`, `AssignedTeamId` | "My tickets", agent queue, team queue |
| `TicketComments` | `(TicketId, CreatedAt)`, `(TicketId, IsInternal)` | Timeline read, internal visibility filter |
| `TicketAssignmentHistory` | `(TicketId, ChangedAt)` | History read |
| `RefreshTokens` | `TokenHash` (unique), `ExpiresAt` | Refresh lookup, cleanup sweep |
| `Notifications` | `(UserId, IsRead)`, `CreatedAt` | Inbox read, retention purge |
| `AuditLogs` | `OccurredAt`, `(EntityName, EntityId)`, `Action`, `UserId` | Audit search (no FK to `Users` by design) |
