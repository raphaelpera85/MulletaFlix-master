using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Data.Enums;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Providers.Books.OpenLibrary
{
    public class OpenLibraryProvider : IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OpenLibraryProvider> _logger;

        public OpenLibraryProvider(IHttpClientFactory httpClientFactory, ILogger<OpenLibraryProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public string Name => "Open Library";

        public int Order => 0;

        public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book>();
            var isbn = info.GetProviderId("ISBN");

            if (!string.IsNullOrWhiteSpace(isbn))
            {
                result = await GetMetadataByIsbn(isbn, cancellationToken).ConfigureAwait(false);
                if (result.HasMetadata)
                {
                    result.QueriedById = true;
                    return result;
                }
            }

            var openLibraryId = info.GetProviderId("OpenLibrary");
            if (!string.IsNullOrWhiteSpace(openLibraryId))
            {
                result = await GetMetadataByOpenLibraryId(openLibraryId, cancellationToken).ConfigureAwait(false);
                if (result.HasMetadata)
                {
                    result.QueriedById = true;
                    return result;
                }
            }

            if (!string.IsNullOrWhiteSpace(info.Name))
            {
                result = await GetMetadataBySearch(info.Name, cancellationToken).ConfigureAwait(false);
            }

            return result;
        }

        private async Task<MetadataResult<Book>> GetMetadataByIsbn(string isbn, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book>();
            var cleanIsbn = isbn.Replace("-", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
            var url = $"https://openlibrary.org/api/books?bibkeys=ISBN:{cleanIsbn}&format=json&jscmd=data";

            try
            {
                using var response = await _httpClientFactory.CreateClient(NamedClient.Default)
                    .GetAsync(url, cancellationToken)
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                if (data is not null && data.TryGetValue($"ISBN:{cleanIsbn}", out var bookEntry))
                {
                    PopulateMetadata(result, bookEntry);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching OpenLibrary metadata by ISBN {Isbn}", isbn);
            }

            return result;
        }

        private async Task<MetadataResult<Book>> GetMetadataByOpenLibraryId(string olId, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book>();
            var normalizedId = NormalizeOpenLibraryId(olId);
            var isWorkId = normalizedId.EndsWith('W');
            var url = isWorkId
                ? $"https://openlibrary.org/works/{normalizedId}.json"
                : $"https://openlibrary.org/api/books?bibkeys=OLID:{normalizedId}&format=json&jscmd=data";

            try
            {
                using var response = await _httpClientFactory.CreateClient(NamedClient.Default)
                    .GetAsync(url, cancellationToken)
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (isWorkId)
                {
                    using var document = JsonDocument.Parse(json);
                    PopulateMetadataFromWork(result, document.RootElement);
                }
                else
                {
                    var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                    if (data is not null && data.TryGetValue($"OLID:{normalizedId}", out var bookEntry))
                    {
                        PopulateMetadata(result, bookEntry);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching OpenLibrary metadata by OLID {Id}", olId);
            }

            return result;
        }

        private async Task<MetadataResult<Book>> GetMetadataBySearch(string title, CancellationToken cancellationToken)
        {
            var results = await GetSearchResults(new BookInfo { Name = title }, cancellationToken).ConfigureAwait(false);
            var best = results.FirstOrDefault();
            if (best is not null && best.ProviderIds.TryGetValue("ISBN", out var isbn))
            {
                return await GetMetadataByIsbn(isbn, cancellationToken).ConfigureAwait(false);
            }

            if (best is not null && best.ProviderIds.TryGetValue("OpenLibrary", out var openLibraryId))
            {
                return await GetMetadataByOpenLibraryId(openLibraryId, cancellationToken).ConfigureAwait(false);
            }

            return new MetadataResult<Book>();
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            var query = searchInfo.Name;

            if (!string.IsNullOrWhiteSpace(searchInfo.SeriesName))
            {
                query = $"{searchInfo.SeriesName} {query}";
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return results;
            }

            var url = $"https://openlibrary.org/search.json?q={query.Replace(" ", "+", StringComparison.Ordinal)}&limit=10";

            try
            {
                using var response = await _httpClientFactory.CreateClient(NamedClient.Default)
                    .GetAsync(url, cancellationToken)
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var searchResult = JsonSerializer.Deserialize<OpenLibrarySearchResult>(json);

                if (searchResult?.Docs is not null)
                {
                    foreach (var doc in searchResult.Docs)
                    {
                        var item = new RemoteSearchResult
                        {
                            Name = doc.Title,
                            SearchProviderName = Name
                        };

                        if (doc.FirstPublishYear > 0)
                        {
                            item.ProductionYear = doc.FirstPublishYear;
                        }

                        var isbns = new List<string>();
                        if (doc.Isbn is not null)
                        {
                            isbns.AddRange(doc.Isbn);
                        }

                        if (isbns.Count > 0)
                        {
                            item.SetProviderId("ISBN", isbns[0]);
                        }

                        if (!string.IsNullOrWhiteSpace(doc.CoverEditionKey))
                        {
                            item.SetProviderId("OpenLibrary", doc.CoverEditionKey);
                            item.ImageUrl = $"https://covers.openlibrary.org/b/olid/{doc.CoverEditionKey}-M.jpg";
                        }
                        else if (doc.CoverId > 0)
                        {
                            item.ImageUrl = $"https://covers.openlibrary.org/b/id/{doc.CoverId}-M.jpg";
                        }
                        else if (doc.Isbn is not null && doc.Isbn.Length > 0 && !string.IsNullOrWhiteSpace(doc.Isbn[0]))
                        {
                            item.ImageUrl = $"https://covers.openlibrary.org/b/isbn/{doc.Isbn[0]}-M.jpg";
                        }

                        if (!string.IsNullOrWhiteSpace(doc.Key) && string.IsNullOrWhiteSpace(item.GetProviderId("OpenLibrary")))
                        {
                            var olid = doc.Key.Replace("/works/", string.Empty, StringComparison.Ordinal);
                            item.SetProviderId("OpenLibrary", olid);
                        }

                        results.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching OpenLibrary for {Query}", query);
            }

            return results;
        }

        private void PopulateMetadata(MetadataResult<Book> result, JsonElement bookEntry)
        {
            result.Item = new Book();
            result.HasMetadata = true;

            if (bookEntry.TryGetProperty("title", out var title))
            {
                result.Item.Name = title.GetString() ?? string.Empty;
            }

            if (bookEntry.TryGetProperty("subtitle", out var subtitle))
            {
                var sub = subtitle.GetString();
                if (!string.IsNullOrWhiteSpace(sub))
                {
                    result.Item.Name = $"{result.Item.Name}: {sub}";
                }
            }

            if (bookEntry.TryGetProperty("publish_date", out var pubDate))
            {
                var dateStr = pubDate.GetString();
                if (!string.IsNullOrWhiteSpace(dateStr))
                {
                    if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    {
                        result.Item.PremiereDate = date;
                        result.Item.ProductionYear = date.Year;
                    }
                    else if (int.TryParse(dateStr, out var year))
                    {
                        result.Item.ProductionYear = year;
                    }
                }
            }

            if (bookEntry.TryGetProperty("authors", out var authors))
            {
                foreach (var author in authors.EnumerateArray())
                {
                    if (author.TryGetProperty("name", out var authorName))
                    {
                        result.AddPerson(new PersonInfo
                        {
                            Name = authorName.GetString() ?? string.Empty,
                            Type = PersonKind.Author
                        });
                    }
                }
            }

            if (bookEntry.TryGetProperty("publishers", out var publishers))
            {
                foreach (var publisher in publishers.EnumerateArray())
                {
                    if (publisher.TryGetProperty("name", out var publisherName))
                    {
                        result.Item.AddStudio(publisherName.GetString() ?? string.Empty);
                    }
                }
            }

            if (bookEntry.TryGetProperty("subjects", out var subjects))
            {
                foreach (var subject in subjects.EnumerateArray())
                {
                    if (subject.TryGetProperty("name", out var subjectName))
                    {
                        var name = subjectName.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Item.AddGenre(name);
                        }
                    }
                }
            }

            if (bookEntry.TryGetProperty("number_of_pages", out var pages))
            {
                result.Item.RunTimeTicks = TimeSpan.FromMinutes(pages.GetInt32()).Ticks;
            }

            if (bookEntry.TryGetProperty("identifiers", out var identifiers))
            {
                if (identifiers.TryGetProperty("isbn_13", out var isbn13) && isbn13.GetArrayLength() > 0)
                {
                    var isbnStr = isbn13[0].GetString();
                    if (!string.IsNullOrWhiteSpace(isbnStr))
                    {
                        result.Item.SetProviderId("ISBN", isbnStr!);
                    }
                }
                else if (identifiers.TryGetProperty("isbn_10", out var isbn10) && isbn10.GetArrayLength() > 0)
                {
                    var isbnStr = isbn10[0].GetString();
                    if (!string.IsNullOrWhiteSpace(isbnStr))
                    {
                        result.Item.SetProviderId("ISBN", isbnStr!);
                    }
                }

                if (identifiers.TryGetProperty("openlibrary", out var ol) && ol.GetArrayLength() > 0)
                {
                    var olStr = ol[0].GetString();
                    if (!string.IsNullOrWhiteSpace(olStr))
                    {
                        result.Item.SetProviderId("OpenLibrary", olStr!);
                    }
                }

                if (identifiers.TryGetProperty("google", out var google) && google.GetArrayLength() > 0)
                {
                    var googleStr = google[0].GetString();
                    if (!string.IsNullOrWhiteSpace(googleStr))
                    {
                        result.Item.SetProviderId("GoogleBooks", googleStr!);
                    }
                }
            }

            if (bookEntry.TryGetProperty("cover", out var cover))
            {
                if (cover.TryGetProperty("large", out var largeCover))
                {
                    var coverUrl = largeCover.GetString();
                    if (!string.IsNullOrWhiteSpace(coverUrl))
                    {
                        result.RemoteImages.Add((coverUrl, ImageType.Primary));
                    }
                }
                else if (cover.TryGetProperty("medium", out var mediumCover))
                {
                    var coverUrl = mediumCover.GetString();
                    if (!string.IsNullOrWhiteSpace(coverUrl))
                    {
                        result.RemoteImages.Add((coverUrl, ImageType.Primary));
                    }
                }
            }
        }

        private void PopulateMetadataFromWork(MetadataResult<Book> result, JsonElement workEntry)
        {
            result.Item = new Book();
            result.HasMetadata = true;

            if (workEntry.TryGetProperty("title", out var title))
            {
                result.Item.Name = title.GetString() ?? string.Empty;
            }

            if (workEntry.TryGetProperty("description", out var description))
            {
                if (description.ValueKind == JsonValueKind.String)
                {
                    var value = description.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Item.Overview = value;
                    }
                }
                else if (description.ValueKind == JsonValueKind.Object
                    && description.TryGetProperty("value", out var descriptionValue))
                {
                    var value = descriptionValue.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Item.Overview = value;
                    }
                }
            }

            if (workEntry.TryGetProperty("first_publish_date", out var firstPublishDate))
            {
                var dateStr = firstPublishDate.GetString();
                if (!string.IsNullOrWhiteSpace(dateStr)
                    && DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    result.Item.PremiereDate = date;
                    result.Item.ProductionYear = date.Year;
                }
            }

            if (workEntry.TryGetProperty("subjects", out var subjects))
            {
                foreach (var subject in subjects.EnumerateArray())
                {
                    var subjectName = subject.GetString();
                    if (!string.IsNullOrWhiteSpace(subjectName))
                    {
                        result.Item.AddGenre(subjectName);
                    }
                }
            }

            if (workEntry.TryGetProperty("covers", out var covers) && covers.ValueKind == JsonValueKind.Array)
            {
                var coverId = covers.EnumerateArray()
                    .Select(item => item.GetInt32())
                    .FirstOrDefault(id => id > 0);

                if (coverId > 0)
                {
                    result.RemoteImages.Add(($"https://covers.openlibrary.org/b/id/{coverId}-L.jpg", ImageType.Primary));
                }
            }
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
        }

        private static string NormalizeOpenLibraryId(string olId)
        {
            var normalized = olId.Trim();
            normalized = normalized.Replace("https://openlibrary.org/", string.Empty, StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace("/works/", string.Empty, StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace("/books/", string.Empty, StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace(".json", string.Empty, StringComparison.OrdinalIgnoreCase);
            return normalized;
        }

        private class OpenLibrarySearchResult
        {
            [JsonPropertyName("docs")]
            public OpenLibraryDoc[]? Docs { get; set; }
        }

        private class OpenLibraryDoc
        {
            [JsonPropertyName("title")]
            public string? Title { get; set; }

            [JsonPropertyName("key")]
            public string? Key { get; set; }

            [JsonPropertyName("author_name")]
            public string[]? AuthorName { get; set; }

            [JsonPropertyName("first_publish_year")]
            public int FirstPublishYear { get; set; }

            [JsonPropertyName("isbn")]
            public string[]? Isbn { get; set; }

            [JsonPropertyName("cover_edition_key")]
            public string? CoverEditionKey { get; set; }

            [JsonPropertyName("cover_i")]
            public int CoverId { get; set; }
        }
    }
}

