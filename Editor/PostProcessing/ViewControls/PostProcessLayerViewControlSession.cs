using System;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed class PostProcessLayerViewControlSession
    {
        private readonly string owner;
        private int targetId;
        private string propertyPath;

        public PostProcessLayerViewControlSession(string owner)
        {
            this.owner = owner;
        }

        public int ActiveHandle;

        public bool IsActive(UnityEngine.Object target, SerializedProperty element)
        {
            return target != null &&
                   element != null &&
                   targetId == target.GetInstanceID() &&
                   propertyPath == element.propertyPath &&
                   PostProcessScreenSpaceViewControl.IsActive(owner);
        }

        public void Start(UnityEngine.Object target, SerializedProperty element, Action<Rect, Event> guiHandler)
        {
            if (target == null || element == null || guiHandler == null)
            {
                return;
            }

            targetId = target.GetInstanceID();
            propertyPath = element.propertyPath;
            ActiveHandle = 0;
            PostProcessScreenSpaceViewControl.Start(owner, guiHandler);
        }

        public void Stop()
        {
            targetId = 0;
            propertyPath = null;
            ActiveHandle = 0;
            PostProcessScreenSpaceViewControl.Stop(owner);
        }

        public void StopIfOwnedBy(UnityEngine.Object target)
        {
            if (target != null && targetId == target.GetInstanceID())
            {
                Stop();
            }
        }

        public bool TryGetElement(out UnityEngine.Object target, out SerializedObject serialized, out SerializedProperty element)
        {
            target = null;
            serialized = null;
            element = null;

            if (targetId == 0 || string.IsNullOrEmpty(propertyPath))
            {
                Stop();
                return false;
            }

            target = EditorUtility.InstanceIDToObject(targetId);
            if (target == null)
            {
                Stop();
                return false;
            }

            serialized = new SerializedObject(target);
            serialized.Update();
            element = serialized.FindProperty(propertyPath);
            if (element != null)
            {
                return true;
            }

            Stop();
            return false;
        }
    }
}
