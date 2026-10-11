using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.Newznab
{
    public class NewznabRequestGenerator : IIndexerRequestGenerator
    {
        protected readonly INewznabCapabilitiesProvider _capabilitiesProvider;
        public int MaxPages { get; set; }
        public int PageSize { get; set; }
        public NewznabSettings Settings { get; set; }
        public int? ProxyId { get; set; }

        public NewznabRequestGenerator(INewznabCapabilitiesProvider capabilitiesProvider)
        {
            _capabilitiesProvider = capabilitiesProvider;

            MaxPages = 30;
            PageSize = 100;
        }

        private bool SupportsSearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings, ProxyId);

                return capabilities.SupportedSearchParameters != null &&
                       capabilities.SupportedSearchParameters.Contains("q");
            }
        }

        protected virtual bool SupportsBookSearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings, ProxyId);

                return capabilities.SupportedBookSearchParameters != null &&
                       capabilities.SupportedBookSearchParameters.Contains("author") &&
                       capabilities.SupportedBookSearchParameters.Contains("title");
            }
        }

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
            var pageableRequests = new IndexerPageableRequestChain();

            var capabilities = _capabilitiesProvider.GetCapabilities(Settings, ProxyId);

            if (capabilities.SupportedBookSearchParameters != null)
            {
                pageableRequests.Add(GetPagedRequests(MaxPages, Settings.Categories, "book", ""));
            }
            else if (capabilities.SupportedSearchParameters != null)
            {
                pageableRequests.Add(GetPagedRequests(MaxPages, Settings.Categories, "search", ""));
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();
            var searchCategories = SearchMediaTypeHelper.FilterCategoriesForMediaType(Settings.Categories, SearchMediaTypeHelper.GetRequestedMediaType(searchCriteria));

            var hasTransliteration = searchCriteria.AuthorQueryTransliterated != null || searchCriteria.BookQueryTransliterated != null;
            var authorQuery = searchCriteria.AuthorQuery;
            var bookQuery = searchCriteria.BookQuery;
            var transliteratedAuthor = searchCriteria.AuthorQueryTransliterated ?? authorQuery;
            var transliteratedBook = searchCriteria.BookQueryTransliterated ?? bookQuery;

            if (SupportsBookSearch)
            {
                AddBookPageableRequests(pageableRequests,
                    searchCategories,
                    $"&author={NewsnabifyTitle(authorQuery)}&title={NewsnabifyTitle(bookQuery)}");

                if (hasTransliteration)
                {
                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        searchCategories,
                        "book",
                        $"&author={NewsnabifyTitle(transliteratedAuthor)}&title={NewsnabifyTitle(transliteratedBook)}"));
                }

                AddBookPageableRequests(pageableRequests,
                    searchCategories,
                    $"&title={NewsnabifyTitle(bookQuery)}");

                if (hasTransliteration)
                {
                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        searchCategories,
                        "book",
                        $"&title={NewsnabifyTitle(transliteratedBook)}"));
                }
            }

            if (SupportsSearch)
            {
                pageableRequests.AddTier();

                pageableRequests.Add(GetPagedRequests(MaxPages,
                    searchCategories,
                    "search",
                    $"&q={NewsnabifyTitle(bookQuery)}+{NewsnabifyTitle(authorQuery)}"));

                if (hasTransliteration)
                {
                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        searchCategories,
                        "search",
                        $"&q={NewsnabifyTitle(transliteratedBook)}+{NewsnabifyTitle(transliteratedAuthor)}"));
                }

                pageableRequests.Add(GetPagedRequests(MaxPages,
                    searchCategories,
                    "search",
                    $"&q={NewsnabifyTitle(authorQuery)}+{NewsnabifyTitle(bookQuery)}"));

                if (hasTransliteration)
                {
                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        searchCategories,
                        "search",
                        $"&q={NewsnabifyTitle(transliteratedAuthor)}+{NewsnabifyTitle(transliteratedBook)}"));
                }

                pageableRequests.AddTier();

                pageableRequests.Add(GetPagedRequests(MaxPages,
                    searchCategories,
                    "search",
                    $"&q={NewsnabifyTitle(bookQuery)}"));

                if (hasTransliteration)
                {
                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        searchCategories,
                        "search",
                        $"&q={NewsnabifyTitle(transliteratedBook)}"));
                }
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(AuthorSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            if (SupportsBookSearch)
            {
                AddBookPageableRequests(pageableRequests,
                    Settings.Categories,
                    $"&author={NewsnabifyTitle(searchCriteria.AuthorQuery)}");

                if (searchCriteria.AuthorQueryTransliterated != null)
                {
                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        Settings.Categories,
                        "book",
                        $"&author={NewsnabifyTitle(searchCriteria.AuthorQueryTransliterated)}"));
                }
            }

            if (SupportsSearch)
            {
                pageableRequests.AddTier();

                pageableRequests.Add(GetPagedRequests(MaxPages,
                    Settings.Categories,
                    "search",
                    $"&q={NewsnabifyTitle(searchCriteria.AuthorQuery)}"));

                if (searchCriteria.AuthorQueryTransliterated != null)
                {
                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        Settings.Categories,
                        "search",
                        $"&q={NewsnabifyTitle(searchCriteria.AuthorQueryTransliterated)}"));
                }
            }

            return pageableRequests;
        }

        private void AddBookPageableRequests(IndexerPageableRequestChain chain, IEnumerable<int> categories, string parameters)
        {
            chain.AddTier();

            chain.Add(GetPagedRequests(MaxPages, categories, "book", $"{parameters}"));
        }

        private IEnumerable<IndexerRequest> GetPagedRequests(int maxPages, IEnumerable<int> categories, string searchType, string parameters)
        {
            if (categories.Empty())
            {
                yield break;
            }

            var categoriesQuery = string.Join(",", categories.Distinct());

            var baseUrl =
                $"{Settings.BaseUrl.TrimEnd('/')}{Settings.ApiPath.TrimEnd('/')}?t={searchType}&cat={categoriesQuery}&extended=1{Settings.AdditionalParameters}";

            if (Settings.ApiKey.IsNotNullOrWhiteSpace())
            {
                baseUrl += "&apikey=" + Settings.ApiKey;
            }

            if (PageSize == 0)
            {
                yield return new IndexerRequest($"{baseUrl}{parameters}", HttpAccept.Rss);
            }
            else
            {
                for (var page = 0; page < maxPages; page++)
                {
                    yield return new IndexerRequest($"{baseUrl}&offset={page * PageSize}&limit={PageSize}{parameters}",
                        HttpAccept.Rss);
                }
            }
        }

        private static string NewsnabifyTitle(string title)
        {
            title = title.Replace("+", " ");
            return Uri.EscapeDataString(title);
        }
    }
}
