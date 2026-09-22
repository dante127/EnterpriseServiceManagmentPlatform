using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentValidation;

namespace ServiceManagement.Application.Common.Validation;

/// <summary>
/// Process-wide FluentValidation defaults. Applied through a module initializer
/// so the API, background workers and tests all report validation errors with
/// camelCase field names that match the JSON the client sent.
/// </summary>
internal static class FluentValidationDefaults
{
    [ModuleInitializer]
    internal static void Initialise()
    {
        ValidatorOptions.Global.PropertyNameResolver = (_, member, _) =>
            member is null ? null : JsonNamingPolicy.CamelCase.ConvertName(member.Name);
    }
}
