using FluentAssertions;
using ServiceManagement.Domain.Sla;

namespace ServiceManagement.UnitTests.Sla;

public class SlaCalculatorTests
{
    private static readonly DateTimeOffset ReportedAt = new(2026, 1, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CalculateDeadlines_uses_the_policy_targets_relative_to_the_report_time()
    {
        var deadlines = SlaCalculator.CalculateDeadlines(ReportedAt, firstResponseMinutes: 30, resolutionMinutes: 240);

        deadlines.FirstResponseDueAt.Should().Be(ReportedAt.AddMinutes(30));
        deadlines.ResolutionDueAt.Should().Be(ReportedAt.AddMinutes(240));
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(-1, 60)]
    [InlineData(30, 0)]
    public void CalculateDeadlines_rejects_non_positive_targets(int firstResponseMinutes, int resolutionMinutes)
    {
        var act = () => SlaCalculator.CalculateDeadlines(ReportedAt, firstResponseMinutes, resolutionMinutes);

        act.Should().Throw<Domain.Exceptions.DomainRuleViolationException>();
    }

    [Fact]
    public void Evaluate_returns_within_sla_early_in_the_window()
    {
        var state = Evaluate(now: ReportedAt.AddMinutes(5), firstRespondedAt: null, resolvedAt: null);

        state.Should().Be(SlaStatus.WithinSla);
    }

    [Fact]
    public void Evaluate_returns_at_risk_once_the_configured_portion_of_the_window_is_used()
    {
        // 30 minute first-response window with a 20% threshold: AtRisk from minute 24.
        var state = Evaluate(now: ReportedAt.AddMinutes(25), firstRespondedAt: null, resolvedAt: null);

        state.Should().Be(SlaStatus.AtRisk);
    }

    [Fact]
    public void Evaluate_returns_breached_when_the_first_response_deadline_passes_unanswered()
    {
        var state = Evaluate(now: ReportedAt.AddMinutes(31), firstRespondedAt: null, resolvedAt: null);

        state.Should().Be(SlaStatus.Breached);
    }

    [Fact]
    public void Evaluate_still_honours_the_resolution_deadline_after_the_first_response()
    {
        // First response was in time, so the clock now measures the resolution window.
        var state = Evaluate(
            now: ReportedAt.AddMinutes(200),
            firstRespondedAt: ReportedAt.AddMinutes(20),
            resolvedAt: null);

        state.Should().Be(SlaStatus.AtRisk);
    }

    [Fact]
    public void Evaluate_returns_breached_once_the_resolution_deadline_passes()
    {
        var state = Evaluate(
            now: ReportedAt.AddMinutes(241),
            firstRespondedAt: ReportedAt.AddMinutes(5),
            resolvedAt: null);

        state.Should().Be(SlaStatus.Breached);
    }

    [Fact]
    public void Evaluate_returns_completed_for_a_resolved_ticket_even_after_the_deadline()
    {
        var state = Evaluate(
            now: ReportedAt.AddMinutes(300),
            firstRespondedAt: ReportedAt.AddMinutes(10),
            resolvedAt: ReportedAt.AddMinutes(250));

        state.Should().Be(SlaStatus.Completed);
    }

    [Fact]
    public void Evaluate_is_a_pure_function_of_its_inputs()
    {
        var first = Evaluate(now: ReportedAt.AddMinutes(25), firstRespondedAt: null, resolvedAt: null);
        var second = Evaluate(now: ReportedAt.AddMinutes(25), firstRespondedAt: null, resolvedAt: null);

        second.Should().Be(first);
    }

    private static SlaStatus Evaluate(
        DateTimeOffset now,
        DateTimeOffset? firstRespondedAt,
        DateTimeOffset? resolvedAt)
    {
        var deadlines = SlaCalculator.CalculateDeadlines(ReportedAt, firstResponseMinutes: 30, resolutionMinutes: 240);

        return SlaCalculator.Evaluate(
            now,
            ReportedAt,
            deadlines.FirstResponseDueAt,
            deadlines.ResolutionDueAt,
            firstRespondedAt,
            resolvedAt,
            atRiskThresholdPercent: 20);
    }
}
