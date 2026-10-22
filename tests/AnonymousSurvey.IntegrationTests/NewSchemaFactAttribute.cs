using AnonymousSurvey.Infrastructure.Persistence;

namespace AnonymousSurvey.IntegrationTests;

/// <summary>
/// <see cref="FactAttribute"/> pomijany, dopóki osadzone skrypty SQL nie zawierają nowego schematu (kolumna <c>Slug</c> w <c>Survey</c>).
/// Po merge'u strumienia A (przebudowa schematu) test uruchamia się sam — nie trzeba zdejmować Skip ręcznie.
/// </summary>
public sealed class NewSchemaFactAttribute : FactAttribute
{
    public NewSchemaFactAttribute()
    {
        if (!SchemaHasSlug.Value)
        {
            Skip = "Wymaga nowego schematu (strumień A: Survey.Slug); uruchomi się po merge'u A.";
        }
    }

    private static readonly Lazy<bool> SchemaHasSlug = new(() =>
    {
        var assembly = typeof(DatabaseInitializer).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql", StringComparison.Ordinal))
            .Any(n =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(n)!);
                return reader.ReadToEnd().Contains("Slug", StringComparison.Ordinal);
            });
    });
}
