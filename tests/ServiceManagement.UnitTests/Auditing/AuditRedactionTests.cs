using FluentAssertions;
using ServiceManagement.Infrastructure.Auditing;

namespace ServiceManagement.UnitTests.Auditing;

/// <summary>
/// Audit redaction: a secret must never survive into the audit trail, no matter
/// how deeply it is nested. The JSON walker originally recursed only into
/// objects, so a sensitive key inside an array element landed in the trail
/// untouched despite the redaction claim.
/// </summary>
public class AuditRedactionTests
{
    [Fact]
    public void A_sensitive_key_at_the_top_level_is_redacted()
    {
        var json = """{"name":"ada","passwordHash":"s3cr3t"}""";

        var redacted = AuditService.Redact(json);

        redacted.Should().Contain("\"***redacted***\"");
        redacted.Should().NotContain("s3cr3t");
    }

    [Fact]
    public void A_sensitive_key_inside_an_array_element_is_redacted()
    {
        var json = """{"changes":[{"field":"credentials","refreshToken":"abc"}],"count":1}""";

        var redacted = AuditService.Redact(json);

        redacted.Should().NotContain("abc", "array elements must be walked like objects");
        redacted.Should().Contain("\"***redacted***\"");
    }

    [Fact]
    public void A_payload_without_sensitive_keys_is_untouched()
    {
        var json = """{"title":"Laptop broken","priority":"P2"}""";

        AuditService.Redact(json).Should().Be(json);
    }

    [Fact]
    public void Non_json_values_pass_through_unchanged()
    {
        const string notJson = "the printer is on fire";

        AuditService.Redact(notJson).Should().Be(notJson);
        AuditService.Redact(null).Should().BeNull();
    }
}
