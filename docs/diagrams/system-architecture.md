# System architecture

```mermaid
flowchart TB
    subgraph Clients["Clients"]
        U[API consumers]
        SW[Swagger UI]
    end

    subgraph Api["ServiceManagement.Api"]
        MW["Middleware<br/>correlation id · exception handler · Serilog · CORS · rate limiting"]
        CTRL["Controllers (versioned /api/v1)<br/>permission-gated, no business logic"]
        AUTHZ["Authorization<br/>permission policy provider + handler"]
        HEALTH["Health probes<br/>/health · /health/live · /health/ready"]
    end

    subgraph App["ServiceManagement.Application"]
        MED["MediatR handlers<br/>commands + queries"]
        PIPE["Pipeline behaviours<br/>validation · logging"]
        DTO["DTOs · specifications · validators"]
    end

    subgraph Dom["ServiceManagement.Domain"]
        AGG["Aggregates<br/>Ticket · User · Team · Service · SlaPolicy"]
        RULES["TicketWorkflow · SlaCalculator · Guard"]
    end

    subgraph Infra["ServiceManagement.Infrastructure"]
        EFC["EF Core<br/>DbContext · configurations · migrations · seeding"]
        SEC["Security<br/>JWT · PBKDF2 hashing · current user"]
        CAC["Cache-aside<br/>distributed cache"]
        JOBS["Background workers<br/>SLA monitor · maintenance + job lock"]
        NOTIF["Notification channels<br/>database + email abstraction"]
        AUD["Audit writer<br/>redaction + correlation"]
    end

    SQL[("SQL Server 2022")]
    REDIS[("Redis 7")]

    U --> MW
    SW --> MW
    MW --> CTRL
    CTRL --> AUTHZ
    CTRL --> MED
    MED --> PIPE --> MED
    MED --> DTO
    MED --> AGG
    AGG --> RULES
    MED -. depends on interfaces implemented by .-> Infra
    EFC --> SQL
    CAC --> REDIS
    JOBS --> EFC
    SEC --> SQL
    NOTIF --> SQL
    AUD --> SQL
    HEALTH --> SQL
    HEALTH --> REDIS
```

## Dependency rule

```mermaid
flowchart LR
    D["Domain<br/>(no package references)"]
    A["Application<br/>MediatR · FluentValidation · Mapster · EF Core abstractions"]
    I["Infrastructure<br/>EF Core SQL Server · Redis · JWT · workers"]
    P["Api<br/>ASP.NET Core · Swashbuckle · Serilog · versioning"]

    A --> D
    I --> A
    P --> I
```

Arrows point **inwards only**. `Domain` cannot see Entity Framework, HTTP or Redis; `Application` cannot
see ASP.NET Core. Cross-boundary work happens through interfaces declared in `Application` and implemented
in `Infrastructure`.
