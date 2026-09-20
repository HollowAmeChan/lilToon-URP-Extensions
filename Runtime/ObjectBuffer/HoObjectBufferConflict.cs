using UnityEngine;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    /// <summary>
    /// 一个 Renderer 被多个部件条目同时命中的记录。
    /// <para>
    /// 成因通常是"拖了父级、展开子级"：父级展开会把整棵子树收进这个部件，
    /// 而子树里某个 Renderer 可能已经被别的部件（或别的角色的 group）显式指定过。
    /// 裁决顺序（确定性的，不依赖遍历顺序之外的东西）：
    /// <b>优先级高者胜 → 优先级相同取层级更近的 group → 同组内取条目顺序在前的那个</b>。
    /// 组件 Inspector 会把这些重复逐条列出来；**不静默吞掉**。
    /// </para>
    /// </summary>
    public readonly struct HoObjectBufferConflict
    {
        public HoObjectBufferConflict(
            Renderer renderer,
            HoObjectBufferGroup winnerGroup,
            int winnerSlot,
            string winnerPartName,
            HoObjectBufferGroup loserGroup,
            int loserSlot,
            string loserPartName)
        {
            Renderer = renderer;
            WinnerGroup = winnerGroup;
            WinnerSlot = winnerSlot;
            WinnerPartName = winnerPartName;
            LoserGroup = loserGroup;
            LoserSlot = loserSlot;
            LoserPartName = loserPartName;
        }

        public readonly Renderer Renderer;

        public readonly HoObjectBufferGroup WinnerGroup;
        public readonly int WinnerSlot;
        public readonly string WinnerPartName;

        public readonly HoObjectBufferGroup LoserGroup;
        public readonly int LoserSlot;
        public readonly string LoserPartName;

        /// <summary>同一个 group 内的条目打架（同组内按条目顺序裁决）。</summary>
        public bool IsSameGroup => WinnerGroup == LoserGroup;

        public bool Involves(HoObjectBufferGroup group)
        {
            return WinnerGroup == group || LoserGroup == group;
        }

        /// <summary>这个部件名在这个 group 里是否参与了冲突（赢或输都算）。</summary>
        public bool InvolvesPart(HoObjectBufferGroup group, string partName)
        {
            if (WinnerGroup == group && WinnerPartName == partName)
            {
                return true;
            }

            return LoserGroup == group && LoserPartName == partName;
        }

        public string Describe()
        {
            string rendererName = Renderer != null ? Renderer.name : "(已销毁)";
            string winner = $"{DescribeGroup(WinnerGroup)} / {WinnerPartName}";
            string loser = $"{DescribeGroup(LoserGroup)} / {LoserPartName}";
            string reason = IsSameGroup ? "同组内按条目顺序取前者" : "对方优先级更高（或层级更近）";
            return $"{rendererName} 同时属于「{winner}」和「{loser}」——{reason}";
        }

        private static string DescribeGroup(HoObjectBufferGroup group)
        {
            return group == null ? "(已销毁)" : $"角色 {group.characterId} · {group.name}";
        }
    }
}
