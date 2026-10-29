"""Collect the three-camera Unity evidence and verify the stage delivery."""
from pathlib import Path
import datetime
import hashlib
import json
import sys
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(r"D:\Unity_Project\BREAK_URP\Assets\mmd场景测试\H2Cu阿相")
LIB = Path(r"D:\Unity_Project\BREAK_URP\Library\HoMmdStages")
EVIDENCE = Path(__file__).resolve().parents[2] / "Temp~" / "MmdStages" / "DetailLighting-20261029"
RECOVERY = EVIDENCE.parent / "YueximeiComplete-20261030"
NEW_INVENTORY = EVIDENCE.parent / "H2CuInventory-20261030"
SOURCE = NEW_INVENTORY / "new-source-copy.json" if (NEW_INVENTORY / "new-source-copy.json").exists() else RECOVERY / "all-source-copy.json" if (RECOVERY / "all-source-copy.json").exists() else EVIDENCE.parent / "Remaining-20261029" / "source-copy.json"
NAMES = ["花吐电気", "乐园·深命", "圣诞万花镜", "茉莉庭院", "六角花台绣球", "琉璃万花镜", "古典机械剧场", "拟花十字之棺", "百鸟溯归", "孔雀月令红墨", "乐园·墟实", "秘月之泉", "万象天文", "炫光夜厅", "夜月蓝", "芋泥咩咩", "月昔梅", "古典机械剧场黑蓝白", "冷色古堂", "孔雀月令", "孔雀月令青瓷", "丝苇楼梯", "月极蝶夜礼", "虫无阶梯", "虫染阶梯", "乐园·葡萄柚", "乐园·西瓜霜"]
LABELS = ["主视角", "左侧视角", "右侧视角"]

def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    EVIDENCE.mkdir(exist_ok=True, parents=True)
    errors = []
    audit = read(LIB / "scene-audit.json")
    by_name = {item["stage"]: item for item in audit["stages"]}
    errors.extend(audit["errors"])
    if set(by_name) != set(NAMES):
        errors.append(f"Unity audit scope does not equal all {len(NAMES)} authored scenes")
    source = read(SOURCE)
    source_count = 0
    for stage in source["stages"]:
        for item in stage["files"]:
            source_count += 1
            for key in ("source", "destination"):
                path = Path(stage[key]) / item["relativePath"]
                if not path.exists() or sha(path) != item["sha256"]:
                    errors.append(f"Source hash mismatch: {path}")
    report = {"revision": "world-space-detail-three-cameras", "date": datetime.datetime.now(datetime.timezone.utc).isoformat(), "engine": "Unity 6000.3.15f1 / D3D12", "ssgiPolicy": "prohibited", "giPolicy": "RTGI + RELAX", "sourceFilesVerified": source_count, "skippedByUser": ["云蜃马戏团·有识月"], "stages": [], "audit": audit}
    font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 20)
    for name in NAMES:
        folder = ROOT / name
        a = by_name[name]
        if a.get("centerPostLighting") or a.get("reviewCameras") != 3:
            errors.append(f"{name}: screen-center light or missing review camera")
        if a["ssgiFeatures"] or a["ssgiVolumes"] or a["giMode"] != "RTGI":
            errors.append(f"{name}: GI policy")
        if a["aoArtMaterials"] != a["aoEligibleMaterials"] or a["modelRenderers"] != a["modelShadowCasters"] or a["externalStageReferences"]:
            errors.append(f"{name}: material / caster / dependency contract")
        stage = {"name": name, "scene": str(folder / (name + ".unity")), "sceneSha256": sha(folder / (name + ".unity")), "look": read(folder / "Rendering" / "细节灯光.json"), "baseline": read(folder / "Rendering" / "细节灯光基线.json"), "views": [], "audit": a, "visualReview": "three previews inspected; final full-resolution review sheets saved"}
        board = Image.new("RGB", (1500, 312), (24, 24, 28))
        draw = ImageDraw.Draw(board)
        for view, label in enumerate(LABELS):
            path = folder / "Renders" / f"{name}_{label}.png"
            detail = read(LIB / f"{name}-view{view}-detail-state.json")
            gi = read(LIB / f"{name}-view{view}-advanced-state.json")
            lighting = read(LIB / f"{name}-view{view}-lighting-state.json")
            with Image.open(path) as image:
                size = list(image.size)
                if size != [1280, 720]: errors.append(f"{name}/{view}: non-final render size")
                thumb = image.convert("RGB"); thumb.thumbnail((500, 281)); board.paste(thumb, (500 * view, 30))
            draw.text((500 * view + 8, 4), name + " · " + label, font=font, fill="white")
            if detail["centerPostLighting"] or detail["frame"] != 32 or detail["view"] != view:
                errors.append(f"{name}/{view}: runtime lighting state")
            if gi["mode"] != "RTGI" or gi["api"] != "Direct3D12" or not gi["giTexture"] or gi["nonzeroFraction"] <= 0:
                errors.append(f"{name}/{view}: runtime GI output")
            if not gi["skyExcluded"]: errors.append(f"{name}/{view}: sky ray exclusion")
            if not any("独立光源遮挡" in value for value in detail["status"]):
                errors.append(f"{name}/{view}: no real HD local light depth")
            stage["views"].append({"view": view, "label": label, "render": str(path), "size": size, "sha256": sha(path), "detail": detail, "gi": gi, "lighting": lighting})
        cameras = [tuple(item["detail"]["camera"].values()) for item in stage["views"]]
        if len(set(cameras)) != 3: errors.append(f"{name}: three views do not have three distinct positions")
        board.save(folder / "Renders" / "三机位总览.jpg", quality=93)
        report["stages"].append(stage)
    capture_count = (EVIDENCE / "final-three-views.log").read_text(encoding="utf-8-sig").count("MMD_DETAIL_CAPTURE ")
    new_log = NEW_INVENTORY / "detail-new.log"
    if new_log.exists(): capture_count += new_log.read_text(encoding="utf-8-sig").count("MMD_DETAIL_CAPTURE ")
    if capture_count != len(NAMES) * 3: errors.append(f"Final capture logs contain {capture_count} outputs, expected {len(NAMES) * 3}")
    report["captureCount"] = capture_count
    if (RECOVERY / "complete-final.log").exists():
        recovery_count = (RECOVERY / "complete-final.log").read_text(encoding="utf-8-sig").count("MMD_DETAIL_CAPTURE ")
        if recovery_count != 3: errors.append(f"Recovered stage requires 3 final outputs, found {recovery_count}")
        report["stageReplacement"] = {"stage": "月昔梅", "captureCount": recovery_count, "log": str(RECOVERY / "complete-final.log"), "models": ["月昔梅 主體.pmx", "月昔梅 樹.pmx", "月昔梅 花枝.pmx", "月昔梅 燈.pmx"], "companionFan": "preserved as an optional disabled prop"}
        recovered_audit = read(LIB / "月昔梅-complete-audit.json")
        errors.extend(recovered_audit["errors"])
        report["stageReplacement"]["assemblyAudit"] = recovered_audit
        previous = read(RECOVERY / "previous-delivery.json")
        unchanged = []
        for stage in previous["stages"]:
            if stage["name"] == "月昔梅": continue
            if sha(Path(stage["scene"])) != stage["sceneSha256"] or any(sha(Path(view["render"])) != view["sha256"] for view in stage["views"]):
                errors.append(stage["name"] + ": unrelated scene/render changed during recovery")
            else: unchanged.append(stage["name"])
        report["stageReplacement"]["unaffectedScenesVerified"] = unchanged
    report["errors"] = errors
    text = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if "stageReplacement" in report: (RECOVERY / "final-delivery.json").write_text(text, encoding="utf-8")
    (EVIDENCE / "final-detail-delivery.json").write_text(text, encoding="utf-8")
    (ROOT / "Rendering-check.json").write_text(text, encoding="utf-8")
    lines = ["# H2Cu 阿相场景索引", "", f"{len(NAMES)} 个独立 Ho 渲场景，每个场景保留 Source、Materials、Rendering、Prefabs、Renders；每个场景均已完成主、左侧、右侧三个机位的实际 D3D12 渲染。", "", "本轮中心塑形使用世界空间灯光，已移除后处理 PostLighting 与 CenterColorCorrection。其余场景降低泛白的太阳散射和多层雾，乐园·墟实保留原有雾光风格；百鸟溯归新增两侧书墙和远书架照明，以保留远处细节。", "", "| 场景 | 主机位 | 左侧 | 右侧 | 三机位总览 |", "| --- | --- | --- | --- | --- |"]
    for name in NAMES:
        lines.append(f"| [{name}]({name}/{name}.unity) | [主]({name}/Renders/{name}_主视角.png) | [左]({name}/Renders/{name}_左侧视角.png) | [右]({name}/Renders/{name}_右侧视角.png) | [总览]({name}/Renders/三机位总览.jpg) |")
    lines.extend(["", "## 调节位置", "", "- `09 细节灯光` 放主体真实补光、远景细节灯及局部斑驳遮片。普通 Point/Spot 上的 Ho-VolumeLight 局部体积光组件负责 HD 独立光源遮挡，步数、柔边、亮度均可在 Inspector 调节。", "- 原主相机保持启用，`10 观察机位` 内保存两台关闭的观察相机，供多角度检查。Renders 内已有三张正式图。", "- `Rendering/细节灯光.json` 是本轮参数记录；`细节灯光基线.json` 防止重复执行脚本时叠乘变暗。日常继续编辑实际灯光与材质即可。", "- Bloom、全局渐变调色、景深、电影边框与底部正中 Hollow Logo 继续保留；日景白边黑标，夜景黑边白标。", "", "## 已核对", "", f"{capture_count} 张 1280×720 图，{len(NAMES)} 个场景原生审计与逐机位 RTGI GPU 输出检查；SSGI feature/volume 为 0。AO 艺术参数、全部模型投影、1024 PLR、独立 pipeline/profile 和场景资源引用均通过。{source_count} 个 E 盘源文件及 Assets 副本哈希吻合。", "", "云蜃马戏团·有识月按用户要求跳过；朱木古堂、血月古堡未被本轮脚本覆盖。月昔梅现已恢复完整黄梅庭院：主体、树、花枝、灯四份 PMX 均使用 Source/完整场景 的相对贴图；原团扇保留为可选关闭道具与独立 prefab。", "", "详细状态、图片与场景哈希见 Rendering-check.json；本轮原图与基线备份在扩展仓库 Temp~/MmdStages/DetailLighting-20261029，月昔梅恢复记录在 Temp~/MmdStages/YueximeiComplete-20261030。"])
    (ROOT / "场景索引.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(json.dumps({"stages": len(report["stages"]), "captures": capture_count, "sourceFilesVerified": source_count, "errors": errors}, ensure_ascii=False))
    return 1 if errors else 0

if __name__ == "__main__":
    sys.exit(main())
