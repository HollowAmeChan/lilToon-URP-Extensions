using System;
using System.Collections.Generic;
using UnityEditor;

namespace lilToon.URP.Extensions.Editor.MaterialGradient
{
    internal static class HoMaterialGradientDebounce
    {
        private static readonly Dictionary<string, double> LastApplyTime = new();
        private static readonly Dictionary<string, Action> Pending = new();
        private static bool scheduled;

        public static void Request(string key, double seconds, Action action, bool force = false)
        {
            if (force)
            {
                Execute(key, seconds, action);
                return;
            }

            Pending[key] = action;
            Schedule();
        }

        public static void Cancel(string key)
        {
            Pending.Remove(key);
            LastApplyTime.Remove(key);
        }

        private static void Schedule()
        {
            if (scheduled)
            {
                return;
            }

            scheduled = true;
            EditorApplication.delayCall += Flush;
        }

        private static void Flush()
        {
            scheduled = false;

            foreach (string key in Pending.Keys)
            {
                Execute(key, 0.0, Pending[key]);
            }

            Pending.Clear();
        }

        private static void Execute(string key, double seconds, Action action)
        {
            double now = EditorApplication.timeSinceStartup;
            if (seconds > 0.0 && LastApplyTime.TryGetValue(key, out double last) && now - last < seconds)
            {
                return;
            }

            LastApplyTime[key] = now;
            action?.Invoke();
        }
    }
}
