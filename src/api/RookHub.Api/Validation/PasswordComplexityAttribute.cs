using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Validation;

public class PasswordComplexityAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string password)
            return new ValidationResult("Password is required.");

        if (!password.Any(char.IsUpper))
            return new ValidationResult("Password must contain at least one uppercase letter.");

        if (!password.Any(char.IsLower))
            return new ValidationResult("Password must contain at least one lowercase letter.");

        if (!password.Any(char.IsDigit))
            return new ValidationResult("Password must contain at least one digit.");

        return ValidationResult.Success;
    }
}
