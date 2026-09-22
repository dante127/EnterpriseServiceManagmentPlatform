namespace ServiceManagement.Application.Common.Models;

/// <summary>Query-string parameters shared by every paged endpoint.</summary>
public class PaginationRequest
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    private int _pageSize = DefaultPageSize;

    public int Page { get; set; } = 1;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, MaxPageSize);
    }

    public string? SortBy { get; set; }

    public bool SortDescending { get; set; }

    public int Skip => (Math.Max(Page, 1) - 1) * PageSize;
}
