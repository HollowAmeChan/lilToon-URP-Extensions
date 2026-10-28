using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
namespace lilToon.URP.Extensions.RT
{
    // Runtime lifetime identity, independent of culling/reservoir/atlas slot.
    // A recreated Unity Light gets a new identity even if InstanceID is reused.
    public static class HoRTLightIdentity
    {
        private sealed class Identity { internal readonly long value = Interlocked.Increment(ref next); }
        private static long next;
        private static readonly ConditionalWeakTable<Light, Identity> identities = new();
        public static long Get(Light light) => light == null ? 0 : identities.GetValue(light, _ => new Identity()).value;
    }
}
