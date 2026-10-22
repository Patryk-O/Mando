using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Application.Abstractions;

public interface ISurveyCatalogQuery
{
    /// <summary>
    /// Null dla nieznanego sluga i dla ankiety w statusie Draft. Closed i Active są zwracane.
    /// Wyszukiwanie nie rozróżnia wielkości liter; zwracany jest kanoniczny slug.
    /// </summary>
    Task<SurveyPublicDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Slug ankiety domyślnej, jeśli jest Active; inaczej slug najwcześniej utworzonej Active; inaczej null.
    /// </summary>
    Task<string?> GetDefaultSlugAsync(CancellationToken cancellationToken = default);
}
