using System.Text;

namespace AnonymousSurvey.Application.Common;

public static class SlugGenerator
{
    public const int MinLength = 3;
    public const int MaxLength = 60;

    private const string DefaultSlug = "ankieta";

    public static string FromTitle(string title)
    {
        var normalized = NormalizeTitle(title);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var mapped = Transliterate(ch);
            var isAllowed = mapped is (>= 'a' and <= 'z') or (>= '0' and <= '9');

            if (isAllowed)
            {
                builder.Append(mapped);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');

        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
        }

        if (slug.Length == 0)
        {
            return DefaultSlug;
        }

        return slug.Length < MinLength ? $"{DefaultSlug}-{slug}" : slug;
    }

    public static bool IsValid(string slug)
    {
        if (slug is null || slug.Length < MinLength || slug.Length > MaxLength)
        {
            return false;
        }

        // Ręczny automat zamiast regexu: `$` w .NET dopasowuje się też przed końcowym '\n'.
        var previousWasHyphen = true;
        foreach (var ch in slug)
        {
            if (ch == '-')
            {
                if (previousWasHyphen)
                {
                    return false;
                }

                previousWasHyphen = true;
            }
            else if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                previousWasHyphen = false;
            }
            else
            {
                return false;
            }
        }

        return !previousWasHyphen;
    }

    private static string NormalizeTitle(string? title)
    {
        var value = title ?? string.Empty;

        try
        {
            return value.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            // Pojedyncza (niesparowana) surogata w tytule: normalizacja Unicode jej nie przyjmuje.
            return value.ToLowerInvariant();
        }
    }

    private static char Transliterate(char ch) => ch switch
    {
        'ą' => 'a',
        'ć' => 'c',
        'ę' => 'e',
        'ł' => 'l',
        'ń' => 'n',
        'ó' => 'o',
        'ś' => 's',
        'ź' => 'z',
        'ż' => 'z',
        _ => ch
    };
}
