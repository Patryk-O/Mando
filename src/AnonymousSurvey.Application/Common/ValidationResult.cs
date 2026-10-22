namespace AnonymousSurvey.Application.Common;

public sealed class ValidationResult
{
    private ValidationResult(IReadOnlyList<string> errors)
    {
        Errors = errors;
    }

    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<string> Errors { get; }

    public static ValidationResult Success() => new([]);

    public static ValidationResult Failure(params string[] errors) => new(errors);

    public static ValidationResult Combine(IEnumerable<ValidationResult> results)
    {
        var messages = results.SelectMany(r => r.Errors).ToList();
        return messages.Count == 0 ? Success() : new ValidationResult(messages);
    }
}
