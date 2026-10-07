using lilToon.URP.Extensions.Debugging;
namespace lilToon.URP.Extensions.CharacterSpecialization
{
    public static class HoCharacterSpecializationDebugViewInfo
    {
        public static readonly HoDebugViewInfo[] Views = HoDebugGrid.EnumViews<HoCharacterSpecializationDebugMode>(
            "CharacterSpecialization", "Hidden/lilToon-HoCharacterSpecialization/URP/Composite",
            "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterSpecialization/Shaders/HoCharacterSpecializationComposite.shader",
            "Eye Color", "Eye Alpha", "Eye Reveal", "Hair Shadow", "FHD Source Mask", "FHD Blur Mask", "FHD Blur Color",
            "FHD Mask", "Outline Source", "Outline Blur", "Outline Mask", "Outline Normal", "EnhOutline Src",
            "EnhOutline Blur", "EnhOutline Fog", "Eye Angle", "Ref Frame", "FHD Face Lit", "FHD Blur Lit", "FHD Tint Lit", "FHD Composite");
    }
}
