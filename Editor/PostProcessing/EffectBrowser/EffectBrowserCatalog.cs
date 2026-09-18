using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    /// <summary>
    /// What the effect browser needs to know about one volume editor: its catalog of effects, how to
    /// tell whether an effect already has a layer, and how to add/remove one.
    /// </summary>
    /// <remarks>
    /// The callbacks take the raw effect value (<c>int</c>) so the browser stays independent of the
    /// two effect enums; each editor adapts them to its own type.
    /// </remarks>
    internal sealed class EffectBrowserCatalog
    {
        public EffectBrowserCatalog(
            string stateKey,
            EffectBrowserEntry[] entries,
            Func<int, bool> isPresent,
            Action<int> toggle,
            Action<int> resetToDefaults,
            Func<int, int> layerCount,
            EffectBrowserEntry[] legacyEntries = null)
        {
            StateKey = stateKey ?? string.Empty;
            Entries = entries ?? Array.Empty<EffectBrowserEntry>();
            LegacyEntries = legacyEntries ?? Array.Empty<EffectBrowserEntry>();
            IsPresent = isPresent;
            Toggle = toggle;
            ResetToDefaults = resetToDefaults;
            LayerCount = layerCount;
        }

        /// <summary>Session state key, so ImageProcess and ScreenProcess remember separately.</summary>
        public string StateKey { get; }

        /// <summary>The effects shown in the sidebar, in panel order.</summary>
        public EffectBrowserEntry[] Entries { get; }

        /// <summary>Old/legacy implementations, drawn below the grid when the search is empty.</summary>
        public EffectBrowserEntry[] LegacyEntries { get; }

        /// <summary>True when the effect already has a layer (drives the green icon).</summary>
        public Func<int, bool> IsPresent { get; }

        /// <summary>Add the effect, or remove its layers when it is already present.</summary>
        public Action<int> Toggle { get; }

        /// <summary>Add the effect (if needed) and reset its parameters to the effect defaults.</summary>
        public Action<int> ResetToDefaults { get; }

        /// <summary>How many layers of that effect the list holds (right-click menu + highlight count).</summary>
        public Func<int, int> LayerCount { get; }

        /// <summary>Layers whose effect matches the (normalized) query: the "列表高亮" count.</summary>
        public int CountHighlightedLayers(string normalizedQuery)
        {
            if (string.IsNullOrEmpty(normalizedQuery) || LayerCount == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < Entries.Length; i++)
            {
                if (EffectBrowserSearch.Matches(Entries[i], normalizedQuery))
                {
                    total += Mathf.Max(0, LayerCount(Entries[i].Effect));
                }
            }

            return total;
        }

        /// <summary>True when the layer's effect matches the query; used for the row highlight.</summary>
        public bool LayerEffectMatches(int effect, string normalizedQuery)
        {
            if (string.IsNullOrEmpty(normalizedQuery))
            {
                return false;
            }

            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Effect == effect)
                {
                    return EffectBrowserSearch.Matches(Entries[i], normalizedQuery);
                }
            }

            return false;
        }
    }
}
