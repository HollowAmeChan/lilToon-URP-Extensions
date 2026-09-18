namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    /// <summary>
    /// One row of the effect browser's sidebar: the effect value, what the user sees, and what the
    /// search matches on.
    /// </summary>
    /// <remarks>
    /// Deliberately free of UnityEngine so <c>.codex-research/effect_browser_sim</c> can link this
    /// file (together with <see cref="EffectBrowserSearch"/>) into a dotnet test harness.
    /// </remarks>
    internal readonly struct EffectBrowserEntry
    {
        /// <summary>The effect as its enum value (<c>(int)ImageProcessEffect.X</c>).</summary>
        public readonly int Effect;

        /// <summary>Chinese label drawn in the sidebar and used as the search's primary key.</summary>
        public readonly string Label;

        /// <summary>Enum member name, drawn in tooltips and matched by the search as well.</summary>
        public readonly string EnumName;

        /// <summary>Icon file name inside <c>Editor/ImageProcessIcons</c> (without extension).</summary>
        public readonly string IconName;

        public EffectBrowserEntry(int effect, string label, string enumName, string iconName)
        {
            Effect = effect;
            Label = label ?? string.Empty;
            EnumName = enumName ?? string.Empty;
            IconName = iconName ?? string.Empty;
        }

        /// <summary>Tooltip: label + enum name, so the icon wall stays traceable to the code.</summary>
        public string Tooltip => string.IsNullOrEmpty(EnumName) ? Label : Label + "  (" + EnumName + ")";

        public bool IsValid => Label.Length > 0 || EnumName.Length > 0;
    }
}
