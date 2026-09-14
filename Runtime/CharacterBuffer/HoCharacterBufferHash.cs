namespace lilToon.URP.Extensions.CharacterBuffer
{
    /// <summary>
    /// 名字 hash（FNV-1a 32）。用途仅限于工具对账、AOV manifest 与 debug 显示——
    /// **像素里存的永远是整数 ID，不是 hash**（Cryptomatte 把 hash 位重解释进 float 的做法我们明确不抄，
    /// 见规划 §4.1）。
    /// </summary>
    internal static class HoCharacterBufferHash
    {
        private const uint OffsetBasis = 2166136261u;
        private const uint Prime = 16777619u;

        public static uint Compute(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0u;
            }

            uint hash = OffsetBasis;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                hash ^= (byte)(c & 0xFF);
                hash *= Prime;
                hash ^= (byte)(c >> 8);
                hash *= Prime;
            }

            return hash;
        }

        /// <summary>部件行的稳定名字：角色 ID + 部件名，避免两个角色用同名部件时 hash 相同。</summary>
        public static uint ComputePart(int characterId, string partName)
        {
            return Compute(characterId.ToString() + "/" + (partName ?? string.Empty));
        }
    }
}
