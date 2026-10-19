"""Independent deterministic area quadrature for the planar step002 fixture.

This is a finite rectangle lighting integral, not a replacement renderer or a
reproduction of the GI reservoir. Roughness=1 simplifies GGX D and Smith terms.
Compare 48/96 subdivisions to bound this quadrature's own discretization error.
"""
import json
import sys
from pathlib import Path
import numpy as np


def integrate(subdivisions, albedo, f0):
    centers = (np.arange(subdivisions) + 0.5) / subdivisions
    wall_y, wall_z = np.meshgrid(3 * centers - 1.5, 2 * centers - 2)
    wall = np.stack((np.full(wall_y.size, 1.25), wall_y.ravel(), wall_z.ravel()), axis=1)
    light_delta = np.array([0.25, 0, -1]) - wall
    light_d2 = np.sum(light_delta * light_delta, axis=1)
    light_dir = light_delta / np.sqrt(light_d2[:, None])
    no_l = -light_dir[:, 0]
    attenuation = (1 - (light_d2 / 100**2)**2)**2
    irradiance = 4 * attenuation / light_d2
    xy = ((np.arange(48, 80) + 0.5) / 128 * 2 - 1) * 3 * np.tan(np.deg2rad(20))
    primary_x, primary_y = np.meshgrid(xy, xy)
    primary = np.stack((primary_x.ravel(), primary_y.ravel(), np.zeros(1024)), axis=1)
    total = np.zeros(3)
    for start in range(0, len(primary), 16):
        delta = wall[None, :, :] - primary[start:start + 16, None, :]
        distance2 = np.sum(delta * delta, axis=2)
        towards_wall = delta / np.sqrt(distance2[:, :, None])
        view_dir = -towards_wall
        no_v = towards_wall[:, :, 0]  # wall N=(-1,0,0), V=towards receiver
        half_dir = view_dir + light_dir[None, :, :]
        half_dir /= np.linalg.norm(half_dir, axis=2)[:, :, None]
        vo_h = np.clip(np.sum(view_dir * half_dir, axis=2), 0, 1)
        fresnel = f0 + (1 - f0) * (1 - vo_h)**5
        # At roughness=1: D=1/pi, G_over_NoV=2*NoL/(NoV+NoL).
        specular = fresnel * no_l[None, :] / (2 * np.pi * (no_v + no_l[None, :]))
        radiance = (albedo[None, None, :] * no_l[None, :, None] / np.pi
                    + specular[:, :, None]) * irradiance[None, :, None]
        receiver_brdf_no_l = np.maximum(-towards_wall[:, :, 2], 0) / np.pi
        solid_angle = no_v / distance2 * (6 / subdivisions**2)
        total += np.sum(radiance * (receiver_brdf_no_l * solid_angle)[:, :, None], axis=(0, 1))
    return total / 1024


def main():
    packed = 0x0100C4D4
    reference_albedo = np.array([packed & 2047, (packed >> 11) & 2047, packed >> 22]) / np.array([2047, 2047, 1023])
    models = {
        "ho": (np.array([154, 3, 1]) / 255, 10 / 255),
        "reference": (reference_albedo, (59 / 255)**2.2),
    }
    result = {"diagnosticOnly": True, "protocol": "Independent wall-area integral, identical receiver ROI, actual quantized secondary colors/F0; primary Lambert albedo=1; unoccluded single point; no reservoir or denoiser code"}
    for name, (albedo, f0) in models.items():
        coarse, fine = integrate(48, albedo, f0), integrate(96, albedo, f0)
        result[name] = {"albedo": albedo.tolist(), "f0": float(f0), "mean48": coarse.tolist(), "mean96": fine.tolist(), "quadratureRelativeDelta": ((fine - coarse) / fine).tolist()}
    result["threshold"] = 0.05
    result["comparisons"] = []
    result_root = Path(__file__).parent / "results"
    for name, model in [("ho-no-reuse", "ho"), ("ho-restir", "ho"), ("reference-no-reuse", "reference"), ("reference-restir", "reference")]:
        path = result_root / (name + ".json")
        if not path.exists():
            raise RuntimeError("Capture the actual Unity reports first: " + str(path))
        report = json.loads(path.read_text(encoding="utf-8-sig"))
        measured = np.array([report["rawIndirectMean"][channel] for channel in ("x", "y", "z")])
        expected = np.array(result[model]["mean96"])
        relative = measured / expected - 1
        result["comparisons"].append({"name": name, "capturePass": report["pass"], "measured": measured.tolist(), "expected": expected.tolist(), "relativeError": relative.tolist(), "qualityPass": bool(report["pass"] and np.all(np.abs(relative) <= result["threshold"]))})
    result["hoQualityPass"] = all(c["qualityPass"] for c in result["comparisons"] if c["name"].startswith("ho-"))
    result["referenceQualityPass"] = all(c["qualityPass"] for c in result["comparisons"] if c["name"].startswith("reference-"))
    result["overallPass"] = result["hoQualityPass"] and result["referenceQualityPass"]
    destination = result_root / "quadrature.json"
    destination.parent.mkdir(exist_ok=True)
    destination.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))
    return 0 if result["overallPass"] else 1


if __name__ == "__main__":
    sys.exit(main())
