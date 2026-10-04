using UnityEngine;

namespace lilToon.URP.Extensions.AttributeComposite
{
    public enum HoACQueryKind
    {
        [InspectorName("物体总覆盖率")] TotalCoverage = 0,
        [InspectorName("具名语义")] Semantic = 1,
        [InspectorName("角色组")] Group = 2,
        [InspectorName("完整身份")] Identity = 3,
        [InspectorName("场景几何")] Geometry = 4,
        [InspectorName("描边视觉壳")] Outline = 5,
        [InspectorName("全屏")] Screen = 6
    }

    public enum HoACMaskDomain
    {
        [InspectorName("全屏")] Screen = 0,
        [InspectorName("已登记物体")] Objects = 1,
        [InspectorName("场景几何")] Geometry = 2,
        [InspectorName("描边视觉壳")] Outline = 3
    }

    /// <summary>名字只在 CPU 解析；GPU 接收类型、数值、范围和解析状态，不混用 ID 空间。</summary>
    public readonly struct HoACQueryDescriptor
    {
        public readonly HoACQueryKind Kind;
        public readonly int Value;
        public readonly HoACMaskDomain Domain;
        public readonly int SemanticId;
        public readonly string Error;
        public bool IsValid => SemanticId > 0 && Error == null;
        public bool NeedsIdentity => Kind == HoACQueryKind.TotalCoverage || Kind == HoACQueryKind.Group ||
            Kind == HoACQueryKind.Identity || Domain == HoACMaskDomain.Objects;
        public bool NeedsSelection => Kind == HoACQueryKind.Semantic;
        public bool NeedsGeometry => Kind == HoACQueryKind.Geometry || Domain == HoACMaskDomain.Geometry;
        public bool NeedsOutline => Kind == HoACQueryKind.Outline || Domain == HoACMaskDomain.Outline;
        // w = expected semantic ID (or 1 for other query kinds); 0 = resolution failed.
        public Vector4 ShaderValue => new Vector4((int)Kind, Value, (int)Domain, IsValid ? SemanticId : 0);

        internal string DescribeMissingInput(bool published, Vector4 flags)
        {
            if (!IsValid) return Error ?? "AC 查询尚未解析。";
            if (!published) return "AC 本相机未发布输入，请检查 Feature 启用与顺序。";
            if (NeedsIdentity && flags.x < 0.5f) return "AC 缺少 OB 身份池。";
            if (NeedsSelection && flags.y < 0.5f) return "AC 缺少 Selection 池。";
            if (NeedsGeometry && flags.z < 0.5f) return "AC 缺少 GB 场景几何。";
            if (NeedsOutline && flags.w < 0.5f) return "AC 缺少 GB 描边视觉壳。";
            return null;
        }

        private HoACQueryDescriptor(HoACQueryKind kind, int value, HoACMaskDomain domain, int semanticId, string error)
        {
            Kind = kind; Value = value; Domain = domain; SemanticId = semanticId; Error = error;
        }

        public static HoACQueryDescriptor Resolve(HoACQueryKind kind, string semanticName, int id, HoACMaskDomain domain)
        {
            string error = null;
            int value = id;
            int semanticId = 1;
            if (!System.Enum.IsDefined(typeof(HoACQueryKind), kind) ||
                !System.Enum.IsDefined(typeof(HoACMaskDomain), domain))
                error = "未知的 AC 查询类型或范围。";
            else if (kind == HoACQueryKind.Semantic)
            {
                if (!HoSemanticSchema.TryGetByName(semanticName, out HoSemanticEntry entry))
                    error = $"未声明的语义：'{semanticName}'。";
                else if (entry.laneIndex < 0 || entry.laneIndex >= HoSemanticSchema.ResolvedLaneCount)
                    error = $"语义 '{semanticName}' 尚无 Selection 产出。";
                else { value = entry.laneIndex; semanticId = entry.semanticId; }
            }
            else if (kind == HoACQueryKind.Group && (id < 1 || id > 255))
                error = "组 ID 必须在 1..255。";
            else if (kind == HoACQueryKind.Identity && (id < 256 || id > 65535))
                error = "完整身份必须包含非零组 ID；部件槽位 0 是合法值。";
            return new HoACQueryDescriptor(kind, value, domain, semanticId, error);
        }
    }
}
