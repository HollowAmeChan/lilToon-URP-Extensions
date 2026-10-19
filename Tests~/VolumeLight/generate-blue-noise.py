"""Generate our 64x64 tileable scalar void-and-cluster mask (numpy + Pillow).

Run from the repository root. The output is data for ray-step stratification,
not a density texture. Fixed seed and toroidal Gaussian keep generation repeatable.
"""
from pathlib import Path
import numpy as np
from PIL import Image

n = 64
rng = np.random.default_rng(7419)
axis = np.minimum(np.arange(n), n - np.arange(n))
kernel = np.exp(-(axis[:, None] ** 2 + axis[None, :] ** 2) / (2 * 1.5**2))
pattern = np.zeros((n, n), dtype=bool)
pattern.flat[rng.choice(n * n, n * n // 10, replace=False)] = True
density = np.fft.ifft2(np.fft.fft2(pattern) * np.fft.fft2(kernel)).real


def influence(index):
    return np.roll(np.roll(kernel, index // n, axis=0), index % n, axis=1)


for _ in range(n * n * 4):
    cluster = int(np.argmax(np.where(pattern, density, -np.inf)))
    pattern.flat[cluster] = False
    density -= influence(cluster)
    void = int(np.argmin(np.where(pattern, np.inf, density)))
    pattern.flat[void] = True
    density += influence(void)
    if cluster == void:
        break
else:
    raise RuntimeError("initial pattern did not converge")

initial = pattern.copy()
initial_density = density.copy()
rank = np.zeros((n, n), dtype=np.int32)
for value in range(int(pattern.sum()) - 1, -1, -1):
    cluster = int(np.argmax(np.where(pattern, density, -np.inf)))
    rank.flat[cluster] = value
    pattern.flat[cluster] = False
    density -= influence(cluster)
pattern = initial
density = initial_density
for value in range(int(pattern.sum()), n * n):
    void = int(np.argmin(np.where(pattern, np.inf, density)))
    rank.flat[void] = value
    pattern.flat[void] = True
    density += influence(void)

output = Path("Runtime/VolumeLight/Resources/HoVolumeLightBlueNoise.png")
Image.fromarray((rank // 16).astype(np.uint8)).save(output)
print(output)
