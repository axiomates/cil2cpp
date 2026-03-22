using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;

/// <summary>
/// Phase D.5 test: FluentValidation 11.11.0 deep validation.
/// Exercises: NotEmpty, InclusiveBetween, EmailAddress, Must (custom predicate),
/// WithErrorCode, ValidateAndThrow, When/Unless conditional rules,
/// MaximumLength, multiple validators, error property access.
///
/// Known gaps (compiler bugs discovered during M6 Phase 2):
///   - AbstractValidator&lt;T&gt; for second type: AbstractValidator&lt;Order&gt; crashes (access
///     violation) when AbstractValidator&lt;Person&gt; already exists. LightCompiler
///     expression tree specialization incomplete for multiple generic type arguments.
///   - RuleForEach: collection validation crashes (generic iterator dispatch issue)
///   - Locale-specific resource strings: FluentValidation uses ResourceManager for
///     default error messages; some messages differ from English defaults in AOT
/// </summary>

public class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}

// Basic validator
public class PersonValidator : AbstractValidator<Person>
{
    public PersonValidator()
    {
        RuleFor(p => p.Name).NotEmpty().WithMessage("Name is required");
        RuleFor(p => p.Age).InclusiveBetween(0, 150).WithMessage("Age must be between 0 and 150");
        RuleFor(p => p.Email).NotEmpty().EmailAddress().WithMessage("Valid email is required");
    }
}

// Extended PersonValidator with Must(), WithErrorCode, MaximumLength, When
public class StrictPersonValidator : AbstractValidator<Person>
{
    public StrictPersonValidator()
    {
        RuleFor(p => p.Name)
            .NotEmpty().WithMessage("Name is required").WithErrorCode("P001");

        RuleFor(p => p.Name)
            .MaximumLength(50).WithMessage("Name too long").WithErrorCode("P002");

        RuleFor(p => p.Age)
            .InclusiveBetween(0, 150).WithMessage("Age must be between 0 and 150");

        RuleFor(p => p.Age)
            .Must(a => a >= 18).WithMessage("Must be 18+").WithErrorCode("P003")
            .When(p => !string.IsNullOrEmpty(p.Email)); // only validate age when email provided

        RuleFor(p => p.Email)
            .NotEmpty().EmailAddress().WithMessage("Valid email is required");
    }
}

class Program
{
    static void Main()
    {
        Console.WriteLine("=== FluentValidationTest ===");

        int testNum = 0;
        var personValidator = new PersonValidator();

        // Test 1: Valid person
        testNum++;
        try
        {
            var person = new Person { Name = "Alice", Age = 30, Email = "alice@example.com" };
            var result = personValidator.Validate(person);
            Console.WriteLine($"[{testNum}] Valid: {result.IsValid}");
        }
        catch (Exception ex) { Console.WriteLine($"[{testNum}] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // Test 2: Empty name
        testNum++;
        try
        {
            var person = new Person { Name = "", Age = 25, Email = "bob@example.com" };
            var result = personValidator.Validate(person);
            Console.WriteLine($"[{testNum}] Valid: {result.IsValid}, Errors: {result.Errors.Count}");
            if (!result.IsValid)
                Console.WriteLine($"[{testNum}] First error: {result.Errors[0].ErrorMessage}");
        }
        catch (Exception ex) { Console.WriteLine($"[{testNum}] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // Test 3: Invalid age
        testNum++;
        try
        {
            var person = new Person { Name = "Charlie", Age = 200, Email = "c@example.com" };
            var result = personValidator.Validate(person);
            Console.WriteLine($"[{testNum}] Valid: {result.IsValid}, Errors: {result.Errors.Count}");
            if (!result.IsValid)
                Console.WriteLine($"[{testNum}] First error: {result.Errors[0].ErrorMessage}");
        }
        catch (Exception ex) { Console.WriteLine($"[{testNum}] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // Test 4: Invalid email
        testNum++;
        try
        {
            var person = new Person { Name = "Dave", Age = 40, Email = "not-an-email" };
            var result = personValidator.Validate(person);
            Console.WriteLine($"[{testNum}] Valid: {result.IsValid}, Errors: {result.Errors.Count}");
            if (!result.IsValid)
                Console.WriteLine($"[{testNum}] First error: {result.Errors[0].ErrorMessage}");
        }
        catch (Exception ex) { Console.WriteLine($"[{testNum}] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // Test 5: Multiple errors
        testNum++;
        try
        {
            var person = new Person { Name = "", Age = -5, Email = "" };
            var result = personValidator.Validate(person);
            Console.WriteLine($"[{testNum}] Valid: {result.IsValid}, Errors: {result.Errors.Count}");
            foreach (var error in result.Errors)
                Console.WriteLine($"[{testNum}]   - {error.PropertyName}: {error.ErrorMessage}");
        }
        catch (Exception ex) { Console.WriteLine($"[{testNum}] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // Test 6: ToString on validation result
        testNum++;
        try
        {
            var person = new Person { Name = "", Age = 25, Email = "valid@test.com" };
            var result = personValidator.Validate(person);
            Console.WriteLine($"[{testNum}] ToString: {result.ToString().Trim()}");
        }
        catch (Exception ex) { Console.WriteLine($"[{testNum}] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // Test 7: StrictPersonValidator — Must() with When conditional
        testNum++;
        try
        {
            var strict = new StrictPersonValidator();
            var person = new Person { Name = "Alice", Age = 30, Email = "alice@example.com" };
            var result = strict.Validate(person);
            Console.WriteLine($"[{testNum}] Strict valid: {result.IsValid}");
        }
        catch (Exception ex) { Console.WriteLine($"[{testNum}] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // Test 8: Must() — age < 18 with email (conditional fails)
        testNum++;
        try
        {
            var strict = new StrictPersonValidator();
            var person = new Person { Name = "Teen", Age = 15, Email = "teen@test.com" };
            var result = strict.Validate(person);
            var ageError = result.Errors.FirstOrDefault(e => e.ErrorCode == "P003");
            Console.WriteLine($"[{testNum}] Must(18+): {(ageError != null ? "OK" : "FAIL")} ({ageError?.ErrorMessage})");
        }
        catch (Exception ex) { Console.WriteLine($"[{testNum}] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // Test 9: When — age < 18 without email (conditional skipped)
        testNum++;
        try
        {
            var strict = new StrictPersonValidator();
            var person = new Person { Name = "Kid", Age = 10, Email = "" };
            var result = strict.Validate(person);
            var ageError = result.Errors.FirstOrDefault(e => e.ErrorCode == "P003");
            // P003 should NOT fire because email is empty → When condition is false
            // But email-required error will fire
            var emailErrors = result.Errors.Where(e => e.PropertyName == "Email").Count();
            Console.WriteLine($"[{testNum}] When(skip): {(ageError == null ? "OK" : "FAIL")} (emailErrors={emailErrors})");
        }
        catch (Exception ex) { Console.WriteLine($"[{testNum}] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // Test 10: WithErrorCode access
        testNum++;
        try
        {
            var strict = new StrictPersonValidator();
            var person = new Person { Name = new string('A', 60), Age = 15, Email = "x@y.com" };
            var result = strict.Validate(person);
            var codes = result.Errors.Select(e => e.ErrorCode).OrderBy(c => c).ToList();
            Console.WriteLine($"[{testNum}] ErrorCodes: {string.Join(",", codes)}");
        }
        catch (Exception ex) { Console.WriteLine($"[{testNum}] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        // Test 11: ValidateAndThrow
        testNum++;
        try
        {
            var person = new Person { Name = "", Age = 25, Email = "valid@test.com" };
            personValidator.ValidateAndThrow(person);
            Console.WriteLine($"[{testNum}] ValidateAndThrow: FAIL (should have thrown)");
        }
        catch (ValidationException vex)
        {
            Console.WriteLine($"[{testNum}] ValidateAndThrow: OK (caught {vex.Errors.Count()} errors)");
        }
        catch (Exception ex) { Console.WriteLine($"[{testNum}] ERROR: {ex.GetType().Name}: {ex.Message}"); }

        Console.WriteLine("=== Done ===");
    }
}
