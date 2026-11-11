using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace RentRoad.Localization;

/// <summary>Formats failed entity annotations using shared resources without changing their validation rules or schema metadata.</summary>
internal static class ResourceValidation
{
    /// <summary>Resolves the existing required and length constraint failures while preserving structured field identifiers.</summary>
    public static string Message(object instance, string field)
    {
        var property = instance.GetType().GetProperty(field);
        var name = SharedResource.Get("Validation.Field" + field);
        var failed = property?.GetCustomAttributes<ValidationAttribute>()
            .FirstOrDefault(attribute => !attribute.IsValid(property.GetValue(instance)));
        return failed switch
        {
            RequiredAttribute => SharedResource.Get("Validation.Required", name),
            StringLengthAttribute length => SharedResource.Get("Validation.MaximumLength", name, length.MaximumLength),
            _ => SharedResource.Get("Validation.InvalidField", name)
        };
    }
}
