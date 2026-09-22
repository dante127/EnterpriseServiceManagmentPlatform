namespace ServiceManagement.Application.Catalog;

/// <summary>Service category projection.</summary>
public sealed record ServiceCategoryDto(
    Guid Id,
    string Name,
    string Code,
    string? Description,
    Guid? ParentId,
    bool IsActive);

/// <summary>Priority projection.</summary>
public sealed record ServicePriorityDto(
    Guid Id,
    string Name,
    string Code,
    int Level,
    string? ColorHex,
    bool IsActive);

/// <summary>SLA policy projection.</summary>
public sealed record SlaPolicyDto(
    Guid Id,
    string Name,
    Guid PriorityId,
    string PriorityName,
    int FirstResponseMinutes,
    int ResolutionMinutes,
    int AtRiskThresholdPercent,
    bool IsActive);

/// <summary>Service projection used by the catalog screen and by ticket creation.</summary>
public sealed record ServiceDto(
    Guid Id,
    string Name,
    string Code,
    string? Description,
    Guid CategoryId,
    string CategoryName,
    Guid? DefaultPriorityId,
    Guid? DefaultSlaPolicyId,
    bool IsActive);
