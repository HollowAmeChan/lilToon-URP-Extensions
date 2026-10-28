using System.Collections.Generic;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.RTShadow;
using UnityEditor;
using UnityEngine;
namespace lilToon.URP.Extensions.Editor.RTShadow
{
    [CustomEditor(typeof(HoRTShadowReceiver))]
    public sealed class HoRTShadowReceiverEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("objectGroup"));
            var allLights=serializedObject.FindProperty("receiveAllLights");EditorGUILayout.PropertyField(allLights);
            if(!allLights.boolValue)EditorGUILayout.PropertyField(serializedObject.FindProperty("lights"),true);
            var whole=serializedObject.FindProperty("receiveWholeGroup");EditorGUILayout.PropertyField(whole);
            var group=serializedObject.FindProperty("objectGroup").objectReferenceValue as HoObjectBufferGroup;
            if(group==null)EditorGUILayout.HelpBox("先指定 OB 接收组，物体在该组的「部件 → 渲染器」里配置。RT 接收无需 shadow-map 包围盒。",MessageType.Info);
            else if(!whole.boolValue)
            {
                var selected=serializedObject.FindProperty("receiverParts");var names=new List<string>();
                foreach(var part in group.parts)if(part!=null&&!string.IsNullOrEmpty(part.name)&&!names.Contains(part.name))names.Add(part.name);
                foreach(var name in names)
                {
                    int found=-1;for(int i=0;i<selected.arraySize;i++)if(selected.GetArrayElementAtIndex(i).stringValue==name){found=i;break;}
                    bool next=EditorGUILayout.ToggleLeft(name,found>=0);
                    if(next&&found<0){int i=selected.arraySize;selected.InsertArrayElementAtIndex(i);selected.GetArrayElementAtIndex(i).stringValue=name;}
                    if(!next&&found>=0)selected.DeleteArrayElementAtIndex(found);
                }
                if(names.Count==0)EditorGUILayout.HelpBox("OB 组还没有具名部件。",MessageType.Warning);
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
