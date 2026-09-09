import cv2
import numpy as np
from PIL import Image

img = Image.open("EAA LOGO.jpg")
arr = np.array(img)
h, w, _ = arr.shape

rgb = arr.astype(float)
r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
saturation = np.maximum(np.maximum(r, g), b) - np.minimum(np.minimum(r, g), b)
luma = 0.299 * r + 0.587 * g + 0.114 * b

# 1. Blue wings
blue_mask = (saturation > 25) & (b > r + 15)
y_b, x_b = np.where(blue_mask)
print(f"Blue wings bbox: X=[{x_b.min()}, {x_b.max()}], Y=[{y_b.min()}, {y_b.max()}]")

# 2. Text "Egyptian Aviation Academy": dark navy blue, located in the lower half (Y > 700)
row_grid = np.arange(h)[:, None]
dark_lower = (luma < 130) & (row_grid > 700)
y_t, x_t = np.where(dark_lower)
print(f"Text bbox: X=[{x_t.min()}, {x_t.max()}], Y=[{y_t.min()}, {y_t.max()}]")
