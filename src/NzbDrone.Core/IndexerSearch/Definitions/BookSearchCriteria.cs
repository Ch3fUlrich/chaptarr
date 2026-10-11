using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    public class BookSearchCriteria : SearchCriteriaBase
    {
        private static readonly Regex PlainNumberOrDecimalRegex = new Regex(@"^\d+(\.\d+)?$", RegexOptions.Compiled);

        public string BookTitle { get; set; }
        public int BookYear { get; set; }
        public string BookIsbn { get; set; }
        public string Disambiguation { get; set; }

        // Title-only query: most indexers accept author/title separately or combine them themselves.
        // Search the main title section; the matcher still validates against the monitored edition title.
        // Including the author here can double-apply author terms (hurting recall) and breaks book-search endpoints
        // that expect a clean title (e.g., Newznab's t=book&author=...&title=...).
        public string BookQuery => GetQueryTitle(GetMainSearchTitle(BookTitle, Author?.Name));

        public string SeriesName => Books?.FirstOrDefault()?.SeriesName;
        public string SeriesPosition => Books?.FirstOrDefault()?.SeriesPosition;

        public string SeriesQuery => string.IsNullOrWhiteSpace(SeriesName) ? null : GetQueryTitle(SeriesName);

        public string SeriesTitleQuery
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SeriesName) || string.IsNullOrWhiteSpace(BookTitle))
                {
                    return null;
                }

                string bookQuery;
                try
                {
                    bookQuery = BookQuery;
                }
                catch (ArgumentException)
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(bookQuery))
                {
                    return null;
                }

                return GetQueryTitle(SeriesName) + "+" + bookQuery;
            }
        }

        public string SeriesPositionQuery
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SeriesName) || string.IsNullOrWhiteSpace(SeriesPosition))
                {
                    return null;
                }

                var normalizedPosition = NormalizeSeriesPosition(SeriesPosition);
                if (string.IsNullOrWhiteSpace(normalizedPosition))
                {
                    return null;
                }

                return GetQueryTitle(SeriesName) + "+" + GetQueryTitle(normalizedPosition);
            }
        }

        internal static string NormalizeSeriesPosition(string position)
        {
            if (string.IsNullOrWhiteSpace(position))
            {
                return null;
            }

            var trimmed = position.Trim();
            if (!PlainNumberOrDecimalRegex.IsMatch(trimmed))
            {
                return null;
            }

            if (trimmed.EndsWith(".0"))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 2);
            }

            return trimmed;
        }

        internal static string GetMainSearchTitle(string title, string author)
        {
            var titleWithoutAuthor = RemoveLeadingAuthorPrefix(title, author);
            if (string.IsNullOrWhiteSpace(titleWithoutAuthor))
            {
                return titleWithoutAuthor;
            }

            var mainTitle = titleWithoutAuthor.SplitBookTitle(author).Item1;
            return string.IsNullOrWhiteSpace(mainTitle) ? titleWithoutAuthor : mainTitle;
        }

        internal static string RemoveLeadingAuthorPrefix(string title, string author)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(author))
            {
                return title;
            }

            var trimmedTitle = title.Trim();
            var trimmedAuthor = author.Trim();
            var prefixes = new[]
            {
                $"{trimmedAuthor}:",
                $"{trimmedAuthor} -",
                $"{trimmedAuthor} –",
                $"{trimmedAuthor} —"
            };

            foreach (var prefix in prefixes)
            {
                if (trimmedTitle.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmedTitle.Substring(prefix.Length).Trim();
                }
            }

            return title;
        }

        public override string ToString()
        {
            return $"[{Author.Name} - {BookTitle}]";
        }
    }
}
