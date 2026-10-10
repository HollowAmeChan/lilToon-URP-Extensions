using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.GeometryData;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.PostProcessing;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class ObjectSelectionRegressionTests
    {
        readonly List<GameObject> objects = new List<GameObject>();
        GameObject Create(string name)
        {
            var go = new GameObject(name); objects.Add(go); return go;
        }
        static uint Identity(Renderer renderer) => HoGeometryRendererBinding.GetManagedValue(renderer) & 0xffffu;
        static bool Member(Renderer renderer, string name) => HoObjectBufferRegistry.IsSelectionMember(Identity(renderer), HoObjectBufferRegistry.GetSelectionId(name));

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            HoObjectBufferRegistry.EnsureBuilt();
        }

        [Test]
        public void SubsetOfSharedPartOverlapsAcrossIdentityGroupsAndCleansUp()
        {
            var a = Create("A").AddComponent<MeshRenderer>();
            var b = Create("B").AddComponent<MeshRenderer>();
            var c = Create("C").AddComponent<SkinnedMeshRenderer>();
            var owner = Create("Owner").AddComponent<HoObjectBufferGroup>();
            var other = Create("Other").AddComponent<HoObjectBufferGroup>();
            owner.parts.Add(new HoObjectBufferPartEntry { name="Body", tags=HoObjectBufferPartTags.Body, renderers=new Object[]{a,b} });
            other.parts.Add(new HoObjectBufferPartEntry { name="Eye", tags=HoObjectBufferPartTags.Eye, renderers=new Object[]{c} });
            var x = new HoObjectBufferSelectionEntry { name="Selection subset X", renderers=new Object[]{a,c} };
            owner.selections.Add(x);
            owner.selections.Add(new HoObjectBufferSelectionEntry { name="Selection subset Y", renderers=new Object[]{a,b} });
            owner.Apply();
            uint body = HoObjectBufferRegistry.GetPartId(owner.groupId,"Body");
            Assert.That(Identity(a), Is.Not.EqualTo(Identity(b)), "Shared parts need distinct membership transport");
            Assert.That(HoObjectBufferRegistry.GetCanonicalPartId(Identity(a)), Is.EqualTo(body));
            Assert.That(HoObjectBufferRegistry.GetCanonicalPartId(Identity(b)), Is.EqualTo(body));
            Assert.That(HoObjectBufferRegistry.GetGroupId(Identity(c)), Is.EqualTo((uint)other.groupId));
            Assert.That(Member(a,x.name), Is.True); Assert.That(Member(b,x.name), Is.False); Assert.That(Member(c,x.name), Is.True);
            Assert.That(Member(a,"Selection subset Y") && Member(b,"Selection subset Y"), Is.True);
            HoObjectBufferRegistry.TryGetPartRowData(HoObjectBufferRegistry.GetPartRow(Identity(a)),out var row);
            Assert.That(row.tags, Is.EqualTo((uint)HoObjectBufferPartTags.Body));
            x.renderers = new Object[]{b}; owner.Apply();
            Assert.That(Member(a,x.name), Is.False); Assert.That(Member(b,x.name), Is.True); Assert.That(Member(c,x.name), Is.False);
            owner.selections.Clear(); owner.Apply();
            Assert.That(Identity(a), Is.EqualTo(body)); Assert.That(Identity(b), Is.EqualTo(body));
            Assert.That(HoObjectBufferRegistry.GetSelectionId(x.name), Is.Zero);
        }

        [Test]
        public void SelectionOnlyObjectsRespectChildrenAndPreserveGeometrySlot()
        {
            var root = Create("Selection root"); var child = Create("Selection child"); child.transform.SetParent(root.transform);
            var a = root.AddComponent<MeshRenderer>(); var b = child.AddComponent<MeshRenderer>();
            var owner = Create("Selection only owner").AddComponent<HoObjectBufferGroup>();
            var entry = new HoObjectBufferSelectionEntry { name="Selection children", renderers=new Object[]{root}, includeChildren=false };
            owner.selections.Add(entry);
            object lease = new object();
            var acquire = typeof(HoGeometryRendererBinding).GetMethod("Acquire",BindingFlags.Static|BindingFlags.NonPublic);
            var release = typeof(HoGeometryRendererBinding).GetMethod("Release",BindingFlags.Static|BindingFlags.NonPublic);
            uint slot = (uint)acquire.Invoke(null,new object[]{a,lease});
            try
            {
                owner.Apply(); Assert.That(Member(a,entry.name), Is.True); Assert.That(Identity(b), Is.Zero);
                Assert.That(HoGeometryRendererBinding.GetManagedValue(a) >> 16, Is.EqualTo(slot));
                entry.includeChildren = true; owner.Apply(); Assert.That(Member(b,entry.name), Is.True);
                owner.enabled = false; HoObjectBufferRegistry.EnsureBuilt();
                Assert.That(Identity(a), Is.Zero); Assert.That(Identity(b), Is.Zero);
                Assert.That(HoGeometryRendererBinding.GetManagedValue(a) >> 16, Is.EqualTo(slot));
            }
            finally { release.Invoke(null,new object[]{lease}); }
        }

        [Test]
        public void SameNamedDeclarationsUnionAndTypedQueriesUseIdentityWithoutSemanticLanes()
        {
            var a = Create("Union A").AddComponent<MeshRenderer>(); var b = Create("Union B").AddComponent<MeshRenderer>();
            var owner = Create("Union owner").AddComponent<HoObjectBufferGroup>();
            var other = Create("Union other").AddComponent<HoObjectBufferGroup>();
            const string name = "Selection union";
            owner.selections.Add(new HoObjectBufferSelectionEntry { name=name,renderers=new Object[]{a} });
            other.selections.Add(new HoObjectBufferSelectionEntry { name=name,renderers=new Object[]{b} }); owner.Apply();
            Assert.That(Member(a,name) && Member(b,name), Is.True);
            var query = HoACQueryDescriptor.Resolve(HoACQueryKind.SelectionGroup,name,0,HoACMaskDomain.Screen);
            Assert.That(query.IsValid && query.NeedsIdentity, Is.True);
            Assert.That(query.NeedsSelection, Is.False, "Custom groups do not allocate the eight semantic lanes");
            Assert.That(HoACQueryDescriptor.Resolve(HoACQueryKind.SelectionGroup,"missing",0,HoACMaskDomain.Screen).IsValid, Is.False);
        }

        [Test]
        public void CachedConsumerResolvesNamesAgainWhenRegistryIdsChange()
        {
            var owner = Create("Query cache owner").AddComponent<HoObjectBufferGroup>();
            owner.selections.Add(new HoObjectBufferSelectionEntry { name="Cached selection" }); owner.Apply();
            var layer = new ScreenProcessLayer { maskSource=HoACQueryKind.SelectionGroup,maskSemanticName="Cached selection" };
            var resolve = typeof(ScreenProcessLayer).GetMethod("ResolveMaskQuery",BindingFlags.Instance|BindingFlags.NonPublic);
            var before = (HoACQueryDescriptor)resolve.Invoke(layer,null);
            Assert.That(HoACDebugCatalog.Resolve(HoACDebugCategory.SelectionGroups,layer.maskSemanticName).Value,Is.EqualTo(before.Value));
            owner.selections.Insert(0,new HoObjectBufferSelectionEntry { name="Earlier selection" }); owner.Apply();
            var after = (HoACQueryDescriptor)resolve.Invoke(layer,null);
            Assert.That(after.Value,Is.Not.EqualTo(before.Value));
            Assert.That(after.Value,Is.EqualTo((int)HoObjectBufferRegistry.GetSelectionId(layer.maskSemanticName)));
            Assert.That(HoACDebugCatalog.Resolve(HoACDebugCategory.SelectionGroups,layer.maskSemanticName).Value,Is.EqualTo(after.Value),"Legend cache follows registry revisions");
            owner.selections.RemoveAt(1); owner.Apply();
            Assert.That(((HoACQueryDescriptor)resolve.Invoke(layer,null)).IsValid,Is.False,"Deleted group must not reuse another group's numeric ID");
            Assert.That(HoACDebugCatalog.Resolve(HoACDebugCategory.SelectionGroups,layer.maskSemanticName).IsValid,Is.False);
        }

        [Test]
        public void FullIdentityGroupFailsClosedInsteadOfSelectingSharedPart()
        {
            var a = Create("Overflow selected").AddComponent<MeshRenderer>();
            var b = Create("Overflow sibling").AddComponent<MeshRenderer>();
            var owner = Create("Full group").AddComponent<HoObjectBufferGroup>();
            for (int i = 0; i < 256; i++) owner.parts.Add(new HoObjectBufferPartEntry { name="part " + i,renderers=i==0 ? new Object[]{a,b} : null });
            owner.selections.Add(new HoObjectBufferSelectionEntry { name="Overflow selection",renderers=new Object[]{a} });
            LogAssert.Expect(LogType.Warning,new Regex("选择组有 1 个 Renderer 超出身份槽位预算"));
            owner.Apply();
            Assert.That(Identity(a),Is.EqualTo(Identity(b)));
            Assert.That(Member(a,"Overflow selection") || Member(b,"Overflow selection"),Is.False);
        }

        [Test]
        public void MembershipBitsCrossEveryWordBoundary()
        {
            var a = Create("Many groups renderer").AddComponent<MeshRenderer>();
            var owner = Create("Many groups owner").AddComponent<HoObjectBufferGroup>();
            for (int i = 0; i < 255; i++) owner.selections.Add(new HoObjectBufferSelectionEntry { name="bit " + i,renderers=i%2==0 ? new Object[]{a} : null });
            owner.Apply();
            for (int i = 0; i < 255; i++) Assert.That(Member(a,"bit " + i), Is.EqualTo(i%2==0), "Selection bit " + i);
        }
    }
}
