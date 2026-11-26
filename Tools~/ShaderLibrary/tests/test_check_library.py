import importlib.util
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import uuid

spec=importlib.util.spec_from_file_location("pipeline_library",Path(__file__).resolve().parents[1]/"check_library.py")
checks=importlib.util.module_from_spec(spec);spec.loader.exec_module(checks)

class PipelineLibraryContract(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/"Pipeline";self.root.mkdir()
        (self.root/"package.json").write_text(json.dumps({"name":"jp.lilxyzw.liltoon.urp.extensions"}))
        for name in checks.RTGI_HEADERS:self.asset("ShaderLibrary/RTGI/"+name,"// shared RTGI\n")
        for name in checks.RECEIVER_HEADERS:self.asset("ShaderLibrary/Shadow/Receiver/"+name,"// receiver protocol\n")
        for name in checks.RTGI_ENTRIES:self.source("Runtime/RTGI/Shaders/"+name,"// runtime entry\n")
        self.asset(checks.HOPASS,"float Encoding(){return 1;}\n")
        self.source("Runtime/RTGI/Shaders/Main.compute",'#include "../../../ShaderLibrary/RTGI/HoRTGIGrid.hlsl"\n')
        pairs=[(checks.HOPASS,"ShaderLibrary~/HoPass/Encoding/HoBufferEncoding.hlsl")]
        pairs += [("ShaderLibrary/Shadow/Receiver/"+name,"ShaderLibrary~/Shadow/Receiver/"+name) for name in checks.COMMON_RECEIVERS]
        self.contract={"schema":1,"id":"fixture","headers":[{"id":str(i),"local":local,"authority":authority,
            "sha256":hashlib.sha256((self.root/local).read_bytes()).hexdigest()} for i,(local,authority) in enumerate(pairs)]}
        self.source("Tools~/ShaderLibrary/contract.json",json.dumps(self.contract))

    def source(self,relative,text):
        path=self.root/relative;path.parent.mkdir(parents=True,exist_ok=True);path.write_text(text,encoding="utf-8",newline="\n");return path
    def asset(self,relative,text):
        path=self.source(relative,text)
        for item in [path]+[p for p in path.parents if p!=self.root and p.is_relative_to(self.root)]:
            meta=Path(str(item)+".meta")
            if not meta.exists():meta.write_text("fileFormatVersion: 2\nguid: "+uuid.uuid4().hex+"\n")
        return path
    def snapshot(self):return {str(p.relative_to(self.root)):p.read_bytes() for p in self.root.rglob("*") if p.is_file()}
    def test_complete_layout_passes_without_any_write(self):
        before=self.snapshot();result=checks.check(self.root)
        self.assertTrue(result["passed"],result["errors"]);self.assertEqual(12,result["rtgiHeaders"])
        self.assertEqual(before,self.snapshot())
    def test_missing_header_and_missing_dependency_fail(self):
        (self.root/"ShaderLibrary/RTGI/HoRTGIGrid.hlsl").unlink()
        self.assertTrue(any("missing RTGI library header" in e for e in checks.check(self.root)["errors"]))
        self.source("ShaderLibrary/RTGI/HoRTGIGrid.hlsl",'#include "Missing.hlsl"\n')
        self.assertTrue(any("missing include Missing" in e for e in checks.check(self.root)["errors"]))
    def test_retired_runtime_editor_and_package_qualified_includes_fail(self):
        self.source("Editor/Probe.shader",'#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTGI/Shaders/HoRTGIGrid.hlsl"\n')
        result=checks.check(self.root)
        self.assertTrue(any("retired library include" in e for e in result["errors"]))
        self.source("Runtime/RTGI/Shaders/HoRTGIGrid.hlsl","// accidentally kept duplicate\n")
        self.assertTrue(any("retired RTGI header remains" in e for e in checks.check(self.root)["errors"]))
    def test_comments_and_inactive_historical_snapshots_do_not_count_as_active_includes(self):
        self.source("Editor/Probe.shader",'/* #include "../Runtime/RTGI/Shaders/HoRTGIGrid.hlsl" */\n')
        self.source("Tests~/Archive/Old.shader",'#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTGI/Shaders/HoRTGIGrid.hlsl"\n')
        self.assertTrue(checks.check(self.root)["passed"])
    def test_local_cycles_terminate_and_engine_or_sdk_ownership_boundaries_are_explicit(self):
        self.source("ShaderLibrary/RTGI/HoRTGIGrid.hlsl",'#include "HoRTGIApplicationBridge.hlsl"\n#include "UnityRayTracingMeshUtils.cginc"\n#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"\n#include "../../Runtime/ThirdParty/SDK.hlsl"\n')
        self.source("ShaderLibrary/RTGI/HoRTGIApplicationBridge.hlsl",'#include "HoRTGIGrid.hlsl"\n')
        self.source("Runtime/ThirdParty/SDK.hlsl",'#include "PrivateConditionalSearchPath.h"\n')
        result=checks.check(self.root);self.assertTrue(result["passed"],result["errors"]);self.assertEqual(2,result["externalIncludes"])
    def test_escaping_local_include_is_rejected(self):
        self.source("ShaderLibrary/RTGI/HoRTGIGrid.hlsl",'#include "../../../outside.hlsl"\n')
        self.assertTrue(any("escapes pipeline" in e for e in checks.check(self.root)["errors"]))
    def test_library_guids_are_valid_and_unique_against_other_active_assets(self):
        path=self.root/"ShaderLibrary/RTGI/HoRTGIGrid.hlsl.meta";guid=path.read_text()
        self.source("Runtime/Probe.shader.meta",guid)
        self.assertTrue(any("duplicate library GUID" in e for e in checks.check(self.root)["errors"]))
        path.write_text("guid: "+"a"*31+"\n")
        self.assertTrue(any("invalid library GUID" in e for e in checks.check(self.root)["errors"]))
    def test_receiver_old_include_and_lost_runtime_entry_are_detected(self):
        self.source("Editor/Probe.shader",'#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTShadow/Shaders/HoRTShadowSampling.hlsl"\\n')
        self.assertTrue(any("retired library include" in e for e in checks.check(self.root)["errors"]))
        (self.root/"Runtime/RTGI/Shaders/HoRTGI.raytrace").unlink()
        self.assertTrue(any("missing Runtime RTGI entry HoRTGI.raytrace" in e for e in checks.check(self.root)["errors"]))

    def test_orphan_library_metadata_cannot_hide_a_second_identity(self):
        self.source("ShaderLibrary/RTGI/Missing.hlsl.meta","guid: "+"a"*32+"\\n")
        self.assertTrue(any("orphan library meta" in e for e in checks.check(self.root)["errors"]))

    def test_crlf_library_is_rejected_before_cross_platform_release(self):
        (self.root/"ShaderLibrary/RTGI/HoRTGIGrid.hlsl").write_bytes(b"// CRLF\r\n")
        self.assertTrue(any("canonical LF" in e for e in checks.check(self.root)["errors"]))
    def test_local_common_mirror_edit_fails_without_a_sibling_repository(self):
        path=self.root/checks.HOPASS;path.write_bytes(path.read_bytes()+b"// local drift\n")
        errors=checks.check(self.root)["errors"]
        self.assertTrue(any("local common-library hash drift" in e for e in errors))

    def test_updated_local_hash_does_not_authorize_an_unchanged_sibling_authority(self):
        hoshader=Path(self.temp.name)/"HoShader"
        for entry in self.contract["headers"]:
            path=hoshader/entry["authority"];path.parent.mkdir(parents=True,exist_ok=True)
            path.write_bytes((self.root/entry["local"]).read_bytes())
        local=self.root/checks.HOPASS;local.write_bytes(b"float Encoding(){return 2;}\n")
        self.contract["headers"][0]["sha256"]=hashlib.sha256(local.read_bytes()).hexdigest()
        self.source("Tools~/ShaderLibrary/contract.json",json.dumps(self.contract))
        self.assertTrue(checks.check(self.root)["passed"])
        errors=checks.check(self.root,hoshader)["errors"]
        self.assertTrue(any("HoShader authority hash drift" in e for e in errors))

    def test_contract_cannot_silently_drop_a_shared_protocol(self):
        self.contract["headers"].pop()
        self.source("Tools~/ShaderLibrary/contract.json",json.dumps(self.contract))
        self.assertTrue(any("all four shared headers" in e for e in checks.check(self.root)["errors"]))

    def test_optional_hopass_authority_comparison_is_read_only_and_detects_drift(self):
        authority=Path(self.temp.name)/"HoShader/ShaderLibrary~/HoPass/Encoding/HoBufferEncoding.hlsl"
        authority.parent.mkdir(parents=True);authority.write_bytes((self.root/checks.HOPASS).read_bytes())
        for name in checks.COMMON_RECEIVERS:
            path=Path(self.temp.name)/"HoShader/ShaderLibrary~/Shadow/Receiver"/name
            path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes((self.root/"ShaderLibrary/Shadow/Receiver"/name).read_bytes())
        hoshader=Path(self.temp.name)/"HoShader";before=self.snapshot()
        self.assertTrue(checks.check(self.root,hoshader)["passed"])
        self.assertEqual(before,self.snapshot())
        authority.write_bytes(b"different bytes\n")
        self.assertTrue(any("authority differs" in e for e in checks.check(self.root,hoshader)["errors"]))

if __name__=="__main__":unittest.main()
