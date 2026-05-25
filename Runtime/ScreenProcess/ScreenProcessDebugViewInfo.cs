using lilToon.URP.Extensions.Debugging;

namespace lilToon.URP.Extensions.ScreenProcess
{
    public static class ScreenProcessDebugViewInfo
    {
        public static readonly HoDebugViewInfo[] Views =
        {
            new HoDebugViewInfo(
                "ScreenProcess",
                "screen-process.rule-mask",
                "Rule",
                0,
                string.Empty,
                string.Empty,
                false,
                "Rule mask debug uses each active ScreenProcess layer shader through _LayerRuleDebugOutput; no standalone debug shader is owned by the public debug UI.")
        };
    }
}
