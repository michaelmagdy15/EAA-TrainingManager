import cv2
import numpy as np
from PIL import Image

# Load original high-res image
img = Image.open("EAA LOGO.jpg").convert("RGBA")
arr = np.array(img)
h, w, _ = arr.shape

rgb = arr[:, :, :3].astype(float)
r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]

# Compute luminance and saturation
luma = 0.299 * r + 0.587 * g + 0.114 * b
max_c = np.maximum(np.maximum(r, g), b)
min_c = np.minimum(np.minimum(r, g), b)
saturation = max_c - min_c

# Distance from white
dist_white = np.sqrt((255 - r)**2 + (255 - g)**2 + (255 - b)**2)

# Analyze background: outer area is white (dist_white < 25)
is_outer_white = dist_white < 30

# The brushed metal rectangle has luminance between 180 and 242 and very low saturation (< 12)
is_brushed_metal = (luma > 175) & (luma < 248) & (saturation < 14)

# Print stats
print("Total pixels:", h * w)
print("Outer white pixels:", np.sum(is_outer_white))
print("Brushed metal pixels:", np.sum(is_brushed_metal))
