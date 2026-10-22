using System.Security.Cryptography;

namespace AnonymousSurvey.Infrastructure.Persistence;

/// <summary>
/// Tokeny dostępu: prefiks <c>wyniki-</c> / <c>konfiguracja-</c> i 16 bajtów (32 znaki hex).
/// Kolumna ma NVARCHAR(64); 32 bajty hex (64 znaki) nie mieszczą się razem z prefiksem „konfiguracja-”.
/// Format jest taki sam jak tokeny z seeda.
/// </summary>
public static class AccessTokenGenerator
{
    private const int RandomBytes = 16;

    public static string Results() => "wyniki-" + Hex();

    public static string Config() => "konfiguracja-" + Hex();

    private static string Hex()
    {
        var buffer = new byte[RandomBytes];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToHexString(buffer).ToLowerInvariant();
    }
}
