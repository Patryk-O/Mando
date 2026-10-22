using AnonymousSurvey.Application.Common;

namespace AnonymousSurvey.UnitTests.Common;

public class SlugGeneratorTests
{
    [Theory]
    [InlineData("Ankieta satysfakcji", "ankieta-satysfakcji")]
    [InlineData("Zażółć gęślą jaźń!", "zazolc-gesla-jazn")]
    [InlineData("ŻÓŁĆ ĄĘŚŹŃ", "zolc-aeszn")]
    [InlineData("  --Hello---World--  ", "hello-world")]
    [InlineData("Ankieta 2026", "ankieta-2026")]
    [InlineData("a_b.c/d", "a-b-c-d")]
    [InlineData("abc", "abc")]
    public void FromTitle_transliterates_lowercases_and_collapses_separators(string title, string expected)
    {
        Assert.Equal(expected, SlugGenerator.FromTitle(title));
    }

    [Fact]
    public void FromTitle_handles_decomposed_diacritics()
    {
        // ż, ó, ć zapisane jako litera + znak łączący (NFD).
        Assert.Equal("zolc", SlugGenerator.FromTitle("z\u0307o\u0301\u0142c\u0301"));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("ß")]
    [InlineData("Привет")]
    [InlineData("😀😀")]
    [InlineData("日本語")]
    public void FromTitle_without_usable_characters_returns_default(string title)
    {
        Assert.Equal("ankieta", SlugGenerator.FromTitle(title));
    }

    [Fact]
    public void FromTitle_lone_surrogate_does_not_throw()
    {
        Assert.Equal("a-b", SlugGenerator.FromTitle("a\uD83Db"));
    }

    [Fact]
    public void FromTitle_null_returns_default()
    {
        Assert.Equal("ankieta", SlugGenerator.FromTitle(null!));
    }

    [Theory]
    [InlineData("AB", "ankieta-ab")]
    [InlineData("Ł", "ankieta-l")]
    [InlineData("a", "ankieta-a")]
    [InlineData("  7  ", "ankieta-7")]
    [InlineData("Привет 1", "ankieta-1")]
    [InlineData("😀 ok", "ankieta-ok")]
    public void FromTitle_too_short_result_gets_prefix(string title, string expected)
    {
        Assert.Equal(expected, SlugGenerator.FromTitle(title));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", "script-alert-1-script")]
    [InlineData("'; DROP TABLE Survey; --", "drop-table-survey")]
    [InlineData("../../etc/passwd", "etc-passwd")]
    public void FromTitle_output_contains_only_safe_characters(string title, string expected)
    {
        var slug = SlugGenerator.FromTitle(title);

        Assert.Equal(expected, slug);
        Assert.Matches("^[a-z0-9-]+$", slug);
    }

    [Fact]
    public void FromTitle_long_title_is_truncated_without_trailing_hyphen()
    {
        // Jednostki po 10 znaków ("aaaaaaaaa-"): 60. znak wypada na separatorze.
        var title = string.Join(" ", Enumerable.Repeat("aaaaaaaaa", 20));

        var slug = SlugGenerator.FromTitle(title);

        Assert.True(slug.Length <= SlugGenerator.MaxLength);
        Assert.Equal(59, slug.Length);
        Assert.False(slug.EndsWith('-'));
        Assert.True(SlugGenerator.IsValid(slug));
    }

    [Fact]
    public void FromTitle_100_character_title_is_at_most_60_characters()
    {
        var slug = SlugGenerator.FromTitle(new string('a', 100));

        Assert.Equal(new string('a', SlugGenerator.MaxLength), slug);
    }

    [Fact]
    public void FromTitle_at_max_length_leaves_no_room_for_suffix_so_collision_handling_must_trim()
    {
        var slug = SlugGenerator.FromTitle(new string('a', 100));

        Assert.Equal(SlugGenerator.MaxLength, slug.Length);
        Assert.False(SlugGenerator.IsValid(slug + "-2"));
        var trimmed = slug[..(SlugGenerator.MaxLength - "-2".Length)] + "-2";
        Assert.True(SlugGenerator.IsValid(trimmed));
    }

    [Fact]
    public void FromTitle_exactly_61_characters_is_cut_to_60()
    {
        var slug = SlugGenerator.FromTitle(new string('b', 61));

        Assert.Equal(60, slug.Length);
    }

    [Fact]
    public void FromTitle_always_returns_valid_slug_for_arbitrary_input()
    {
        var random = new Random(12345);
        const string alphabet = "aAbBzZ09 -_ąćęłńóśźżĄĆĘŁŃÓŚŹŻß😀!?<>'\";\t\n\u0301";

        for (var i = 0; i < 2000; i++)
        {
            var chars = new char[random.Next(0, 150)];
            for (var j = 0; j < chars.Length; j++)
            {
                chars[j] = alphabet[random.Next(alphabet.Length)];
            }

            var title = new string(chars);
            var slug = SlugGenerator.FromTitle(title);

            Assert.True(SlugGenerator.IsValid(slug), $"Niepoprawny slug „{slug}” dla tytułu „{title}”.");
        }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("a-b")]
    [InlineData("ab-cd")]
    [InlineData("a1-b2-c3")]
    [InlineData("123")]
    public void IsValid_accepts_proper_slugs(string slug)
    {
        Assert.True(SlugGenerator.IsValid(slug));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("-ab")]
    [InlineData("ab-")]
    [InlineData("ab--cd")]
    [InlineData("Ab")]
    [InlineData("AB-cd")]
    [InlineData("ab_cd")]
    [InlineData("ab cd")]
    [InlineData("żółć")]
    [InlineData("abc\n")]
    [InlineData("ab٣")]
    [InlineData("")]
    public void IsValid_rejects_improper_slugs(string slug)
    {
        Assert.False(SlugGenerator.IsValid(slug));
    }

    [Fact]
    public void IsValid_rejects_null()
    {
        Assert.False(SlugGenerator.IsValid(null!));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void IsValid_enforces_length_boundaries(int length, bool expected)
    {
        Assert.Equal(expected, SlugGenerator.IsValid(new string('a', length)));
    }

    [Fact]
    public void Length_constants_match_plan()
    {
        Assert.Equal(3, SlugGenerator.MinLength);
        Assert.Equal(60, SlugGenerator.MaxLength);
    }
}
