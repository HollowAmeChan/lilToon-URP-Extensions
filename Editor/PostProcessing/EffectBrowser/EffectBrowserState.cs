using UnityEditor;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    /// <summary>
    /// Persistent state of one effect browser: the search text and which of the two sidebar styles is
    /// active. Backed by <see cref="SessionState"/> keyed per editor type, so switching inspectors or
    /// reloading the domain keeps the user's choice (and it never leaks into the asset).
    /// </summary>
    internal sealed class EffectBrowserState
    {
        private const string KeyPrefix = "lilToon.EffectBrowser.";

        private readonly string key;

        private EffectBrowserState(string key, string search, bool iconOnly)
        {
            this.key = key;
            Search = search ?? string.Empty;
            IconOnly = iconOnly;
            Page = 0;
        }

        /// <summary>Current search text (raw, as typed).</summary>
        public string Search { get; set; }

        /// <summary>True = 2 columns of icons; false = one column of icon + name.</summary>
        public bool IconOnly { get; set; }

        /// <summary>Current sidebar page (not persisted: a fresh inspector starts at page 1).</summary>
        public int Page { get; set; }

        /// <summary>Per-editor-type state, restored from the session.</summary>
        public static EffectBrowserState For(string stateKey)
        {
            string key = KeyPrefix + stateKey;
            string search = SessionState.GetString(key + ".search", string.Empty);
            bool iconOnly = SessionState.GetBool(key + ".iconOnly", true);
            return new EffectBrowserState(key, search, iconOnly);
        }

        /// <summary>Writes the persisted half of the state; call once per drawn frame.</summary>
        public void Save()
        {
            SessionState.SetString(key + ".search", Search ?? string.Empty);
            SessionState.SetBool(key + ".iconOnly", IconOnly);
        }

        /// <summary>Clears the query; the page returns to the first one.</summary>
        public void ClearSearch()
        {
            Search = string.Empty;
            Page = 0;
        }
    }
}
