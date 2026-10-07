using lilToon.URP.Extensions.Debugging;
namespace lilToon.URP.Extensions.CharacterShadow
{
    public static class HoCharacterShadowDebugViewInfo
    {
        public static readonly HoDebugViewInfo[] Views = HoDebugGrid.EnumViews<HoCharacterShadowDebugMode>(
            "CharacterShadow", "Hidden/Ho-CharacterShadow/Debug",
            "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterShadow/Shaders/HoCharacterShadowDebug.shader");
    }
}
