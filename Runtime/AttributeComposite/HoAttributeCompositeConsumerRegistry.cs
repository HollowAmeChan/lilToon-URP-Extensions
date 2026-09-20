using System.Collections.Generic;

namespace lilToon.URP.Extensions.AttributeComposite
{
    /// <summary>一个消费者声明：它读了 schema 里哪些名字。</summary>
    public sealed class HoAttributeCompositeConsumerDeclaration
    {
        public HoAttributeCompositeConsumerDeclaration(string consumer, string[] names)
        {
            Consumer = consumer;
            Names = names;
        }

        public string Consumer { get; }

        public string[] Names { get; }

        /// <summary>解析不到的名字（空数组 = 全部解析成功）。</summary>
        public string[] UnresolvedNames
        {
            get
            {
                var unresolved = new List<string>();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!HoSemanticSchema.TryGetByName(Names[i], out _))
                    {
                        unresolved.Add(Names[i]);
                    }
                }

                return unresolved.ToArray();
            }
        }
    }

    /// <summary>
    /// 消费者登记（规划 §3）：**登记用于资源规划与诊断**，HLSL 拦不住没登记的代码直接调函数，
    /// 所以这里的判据是"没登记/解析不到就报出来"，而不是"读不到"。
    /// <para>静态表按消费者名去重（重复声明覆盖旧值），不会随域重载无界增长。</para>
    /// </summary>
    public static class HoAttributeCompositeConsumerRegistry
    {
        private static readonly List<HoAttributeCompositeConsumerDeclaration> Declarations_ =
            new List<HoAttributeCompositeConsumerDeclaration>();

        public static IReadOnlyList<HoAttributeCompositeConsumerDeclaration> Declarations => Declarations_;

        public static void Declare(string consumer, params string[] semanticNames)
        {
            if (string.IsNullOrEmpty(consumer))
            {
                return;
            }

            var names = semanticNames ?? System.Array.Empty<string>();
            for (int i = 0; i < Declarations_.Count; i++)
            {
                if (Declarations_[i].Consumer == consumer)
                {
                    Declarations_[i] = new HoAttributeCompositeConsumerDeclaration(consumer, names);
                    return;
                }
            }

            Declarations_.Add(new HoAttributeCompositeConsumerDeclaration(consumer, names));
        }

        /// <summary>解析不到的名字总数（面板与诊断用；0 = 全部命中 schema）。</summary>
        public static int CountUnresolved()
        {
            int total = 0;
            for (int i = 0; i < Declarations_.Count; i++)
            {
                total += Declarations_[i].UnresolvedNames.Length;
            }

            return total;
        }
    }
}
