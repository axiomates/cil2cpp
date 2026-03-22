using System;
using System.Globalization;
using FluentValidation;

// Simple model classes
public class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}

// Validator using FluentValidation builder pattern
public class PersonValidator : AbstractValidator<Person>
{
    public PersonValidator()
    {
        RuleFor(p => p.Name).NotEmpty().WithMessage("Name is required");
        RuleFor(p => p.Age).InclusiveBetween(0, 150).WithMessage("Age must be between 0 and 150");
        RuleFor(p => p.Email).NotEmpty().EmailAddress().WithMessage("Valid email is required");
    }
}

class Program
{
    static void Main()
    {
        // Force invariant culture so FluentValidation's LanguageManager uses English
        // default messages regardless of system locale. AOT runtime doesn't detect
        // system UI culture, so C++ always produces English — match that here.
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        var validator = new PersonValidator();

        // [1] Valid person
        try
        {
            var person = new Person { Name = "Alice", Age = 30, Email = "alice@example.com" };
            var result = validator.Validate(person);
            Console.WriteLine($"[1] Valid: {result.IsValid}");
        }
        catch (Exception ex) { Console.WriteLine($"[1] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [2] Empty name
        try
        {
            var person = new Person { Name = "", Age = 25, Email = "bob@example.com" };
            var result = validator.Validate(person);
            Console.WriteLine($"[2] Valid: {result.IsValid}, Errors: {result.Errors.Count}");
            if (!result.IsValid)
                Console.WriteLine($"[2] First error: {result.Errors[0].ErrorMessage}");
        }
        catch (Exception ex) { Console.WriteLine($"[2] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [3] Invalid age
        try
        {
            var person = new Person { Name = "Charlie", Age = 200, Email = "c@example.com" };
            var result = validator.Validate(person);
            Console.WriteLine($"[3] Valid: {result.IsValid}, Errors: {result.Errors.Count}");
            if (!result.IsValid)
                Console.WriteLine($"[3] First error: {result.Errors[0].ErrorMessage}");
        }
        catch (Exception ex) { Console.WriteLine($"[3] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [4] Invalid email
        try
        {
            var person = new Person { Name = "Dave", Age = 40, Email = "not-an-email" };
            var result = validator.Validate(person);
            Console.WriteLine($"[4] Valid: {result.IsValid}, Errors: {result.Errors.Count}");
            if (!result.IsValid)
                Console.WriteLine($"[4] First error: {result.Errors[0].ErrorMessage}");
        }
        catch (Exception ex) { Console.WriteLine($"[4] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [5] Multiple errors
        try
        {
            var person = new Person { Name = "", Age = -5, Email = "" };
            var result = validator.Validate(person);
            Console.WriteLine($"[5] Valid: {result.IsValid}, Errors: {result.Errors.Count}");
            foreach (var error in result.Errors)
                Console.WriteLine($"[5]   - {error.PropertyName}: {error.ErrorMessage}");
        }
        catch (Exception ex) { Console.WriteLine($"[5] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // [6] ToString on validation result
        try
        {
            var person = new Person { Name = "", Age = 25, Email = "valid@test.com" };
            var result = validator.Validate(person);
            Console.WriteLine($"[6] ToString: {result.ToString().Trim()}");
        }
        catch (Exception ex) { Console.WriteLine($"[6] ERROR: {ex.GetType().Name}: {ex.Message}"); }
    }
}
