using System;
using System.Collections.Generic;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    /// <summary>
    /// Search, filtering and paging of the effect browser sidebar. Pure C# on purpose: the dotnet
    /// harness in <c>.codex-research/effect_browser_sim</c> links this file and checks the matching
    /// and paging rules directly.
    /// </summary>
    /// <remarks>
    /// Rules (see Documentation~/后处理/EffectBrowser.md §2):
    /// <list type="bullet">
    /// <item>the query matches the <b>effect only</b> - Chinese label or enum member name; preset
    /// names and pinyin deliberately do not participate;</item>
    /// <item>matching is substring, case insensitive, and ignores surrounding whitespace;</item>
    /// <item>an empty query matches everything, in catalog order;</item>
    /// <item>the sidebar shows 3 columns x 20 rows in icon-only style (60 per page - every current
    /// effect fits on one page) and 1 column x 20 rows in the icon+name style (20 per page); both
    /// styles use the same sidebar width, which is why the icon-only style fits three columns.</item>
    /// </list>
    /// </remarks>
    internal static class EffectBrowserSearch
    {
        public const int IconOnlyColumns = 3;
        public const int IconOnlyRows = 20;
        public const int NamedColumns = 1;
        public const int NamedRows = 20;

        /// <summary>Entries per page: 60 in icon-only style (3 x 20), 20 in the icon+name style.</summary>
        public static int PageSize(bool iconOnly)
        {
            return iconOnly ? IconOnlyColumns * IconOnlyRows : NamedColumns * NamedRows;
        }

        /// <summary>Columns drawn per page.</summary>
        public static int Columns(bool iconOnly)
        {
            return iconOnly ? IconOnlyColumns : NamedColumns;
        }

        /// <summary>Rows drawn per page.</summary>
        public static int Rows(bool iconOnly)
        {
            return iconOnly ? IconOnlyRows : NamedRows;
        }

        /// <summary>Query form used for every comparison: trimmed and case folded.</summary>
        public static string Normalize(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return string.Empty;
            }

            return query.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// True when the entry matches the query. Callers normally pass an already normalized query
        /// (see <see cref="Normalize"/>) to keep the per-repaint loop allocation free, but a raw query
        /// is handled too: whitespace-only means "match everything" and case is folded, so a new
        /// caller cannot get a silently wrong answer by forgetting to normalize.
        /// </summary>
        public static bool Matches(EffectBrowserEntry entry, string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return true;
            }

            string normalized = NeedsNormalizing(query) ? Normalize(query) : query;
            if (normalized.Length == 0)
            {
                return true;
            }

            if (entry.Label.Length > 0 && entry.Label.ToLowerInvariant().Contains(normalized))
            {
                return true;
            }

            return entry.EnumName.Length > 0 && entry.EnumName.ToLowerInvariant().Contains(normalized);
        }

        /// <summary>Cheap guard: only a padded or upper-case query pays for <see cref="Normalize"/>.</summary>
        private static bool NeedsNormalizing(string query)
        {
            if (char.IsWhiteSpace(query[0]) || char.IsWhiteSpace(query[query.Length - 1]))
            {
                return true;
            }

            for (int i = 0; i < query.Length; i++)
            {
                if (char.IsUpper(query[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Indices of the matching entries, in catalog order (so the sidebar never reshuffles when
        /// the query changes).
        /// </summary>
        public static void Filter(EffectBrowserEntry[] entries, string query, List<int> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            if (entries == null)
            {
                return;
            }

            string normalized = Normalize(query);
            for (int i = 0; i < entries.Length; i++)
            {
                if (Matches(entries[i], normalized))
                {
                    destination.Add(i);
                }
            }
        }

        /// <summary>Number of pages for a match count; an empty result still has one (empty) page.</summary>
        public static int PageCount(int matchCount, bool iconOnly)
        {
            if (matchCount <= 0)
            {
                return 1;
            }

            int size = PageSize(iconOnly);
            return ((matchCount - 1) / size) + 1;
        }

        /// <summary>Page index clamped into range, so shrinking the result set cannot strand the view.</summary>
        public static int ClampPage(int page, int matchCount, bool iconOnly)
        {
            int count = PageCount(matchCount, iconOnly);
            if (page < 0)
            {
                return 0;
            }

            return page >= count ? count - 1 : page;
        }

        /// <summary>Slice of the match list that the given page shows.</summary>
        public static void PageRange(int page, int matchCount, bool iconOnly, out int start, out int count)
        {
            int clamped = ClampPage(page, matchCount, iconOnly);
            int size = PageSize(iconOnly);
            start = clamped * size;
            count = Math.Max(0, Math.Min(size, matchCount - start));
        }

        /// <summary>"1/3" for the toolbar; always at least "1/1".</summary>
        public static string FormatPageLabel(int page, int matchCount, bool iconOnly)
        {
            int clamped = ClampPage(page, matchCount, iconOnly);
            return (clamped + 1).ToString() + "/" + PageCount(matchCount, iconOnly).ToString();
        }

        /// <summary>Result summary drawn next to the search field.</summary>
        public static string FormatCounts(int matchCount, int highlightCount)
        {
            return highlightCount > 0
                ? "侧栏命中 " + matchCount.ToString() + " · 列表高亮 " + highlightCount.ToString()
                : "侧栏命中 " + matchCount.ToString() + " · 列表里没有";
        }
    }
}
