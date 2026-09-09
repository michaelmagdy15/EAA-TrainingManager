import cv2
import numpy as np
from PIL import Image

# Load original high-res EAA LOGO.jpg
img = Image.open("EAA LOGO.jpg").convert("RGB")
arr = np.array(img)
h, w, _ = arr.shape

out_rgba = np.zeros((h, w, 4), dtype=np.uint8)
out_rgba[:, :, :3] = arr

rgb = arr.astype(float)
r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
luma = 0.299 * r + 0.587 * g + 0.114 * b
saturation = np.maximum(np.maximum(r, g), b) - np.minimum(np.minimum(r, g), b)
blue_diff = b - r

# Final composite alpha
alpha = np.zeros((h, w), dtype=float)

# =========================================================================
# 1. CENTRAL OVAL BADGE (100% Mathematically Perfect Ellipse)
# =========================================================================
cx, cy = 1635.3, 543.8
rx, ry = 307.0, 243.0

y_coords, x_coords = np.ogrid[:h, :w]
ellipse_dist = ((x_coords - cx) / rx) ** 2 + ((y_coords - cy) / ry) ** 2

# Anti-aliasing over 2.5 pixels
badge_alpha = np.clip(1.0 - (ellipse_dist - 0.985) / 0.03, 0.0, 1.0)
alpha = np.maximum(alpha, badge_alpha)

# =========================================================================
# 2. BLUE AERONAUTICAL WINGS
# =========================================================================
wings_region = (y_coords >= 380) & (y_coords <= 720) & (x_coords >= 280) & (x_coords <= 2980)

# Wings are rich blue/cyan: (B - R) > 10 OR (B - G) > 5 OR blue saturation > 15
wing_blue = wings_region & ((blue_diff > 12) | ((b > g + 6) & (b > 75)) | ((saturation > 20) & (b > 85)))

# Wing darker beveled edges (dark blue / navy shadows on the wings themselves)
wing_dark_edges = wings_region & (luma < 140) & (blue_diff > 4)

# Combine
wing_combined = (wing_blue | wing_dark_edges)

# Morphological cleanup
kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))
wing_uint = (wing_combined * 255).astype(np.uint8)
wing_closed = cv2.morphologyEx(wing_uint, cv2.MORPH_CLOSE, kernel)

# Remove tiny noise specks outside wings
num_labels, labels, stats, _ = cv2.connectedComponentsWithStats(wing_closed)
for i in range(1, num_labels):
    if stats[i, cv2.CC_STAT_AREA] < 80:
        wing_closed[labels == i] = 0

# Smooth alpha edge
wing_smooth = cv2.GaussianBlur(wing_closed.astype(float) / 255.0, (5, 5), 0.8)
alpha = np.maximum(alpha, wing_smooth)

# =========================================================================
# 3. TEXT: "Egyptian Aviation Academy" (Pure Navy Blue, Zero Shadow)
# =========================================================================
text_region = (y_coords >= 820) & (y_coords <= 1040) & (x_coords >= 450) & (x_coords <= 2800)

# Critical insight: The shadow is gray (B - R < 10), while the text is navy blue (B - R > 18)!
# Letters also have dark luma < 155
is_text = text_region & (blue_diff > 16) & (luma < 165)

# For slightly lighter blue highlights in the letters, also include if saturation > 25 and b > 90
is_text |= text_region & (saturation > 25) & (b > 90) & (luma < 170) & (blue_diff > 10)

text_uint = (is_text * 255).astype(np.uint8)

# Close small letter internal spaces (like dots on 'i')
kernel_text = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3))
text_closed = cv2.morphologyEx(text_uint, cv2.MORPH_CLOSE, kernel_text)

# Remove any specks smaller than 25 pixels
num_labels_t, labels_t, stats_t, _ = cv2.connectedComponentsWithStats(text_closed)
for i in range(1, num_labels_t):
    if stats_t[i, cv2.CC_STAT_AREA] < 30:
        text_closed[labels_t == i] = 0

# Smooth text with slight anti-aliasing
text_smooth = cv2.GaussianBlur(text_closed.astype(float) / 255.0, (3, 3), 0.5)
alpha = np.maximum(alpha, text_smooth)

# Apply alpha to RGBA
out_rgba[:, :, 3] = np.clip(alpha * 255.0, 0, 255).astype(np.uint8)

# Crop transparent margins
non_zero = np.where(out_rgba[:, :, 3] > 15)
if len(non_zero[0]) > 0:
    min_y, max_y = non_zero[0].min(), non_zero[0].max()
    min_x, max_x = non_zero[1].min(), non_zero[1].max()
    pad = 16
    min_y = max(0, min_y - pad)
    max_y = min(h - 1, max_y + pad)
    min_x = max(0, min_x - pad)
    max_x = min(w - 1, max_x + pad)
    
    final_logo = out_rgba[min_y:max_y+1, min_x:max_x+1]
else:
    final_logo = out_rgba

# Save master high-resolution transparent PNG
Image.fromarray(final_logo).save("EAA_LOGO_Transparent.png")
print("Saved master EAA_LOGO_Transparent.png:", final_logo.shape)

# Also create an Emblem-only transparent version (badge + wings) for header/icon usage
emblem_mask = np.maximum(badge_alpha, wing_smooth)
emblem_rgba = np.zeros((h, w, 4), dtype=np.uint8)
emblem_rgba[:, :, :3] = arr
emblem_rgba[:, :, 3] = np.clip(emblem_mask * 255.0, 0, 255).astype(np.uint8)

non_zero_e = np.where(emblem_rgba[:, :, 3] > 15)
min_y, max_y = non_zero_e[0].min(), non_zero_e[0].max()
min_x, max_x = non_zero_e[1].min(), non_zero_e[1].max()
pad = 12
min_y = max(0, min_y - pad)
max_y = min(h - 1, max_y + pad)
min_x = max(0, min_x - pad)
max_x = min(w - 1, max_x + pad)

emblem_cropped = emblem_rgba[min_y:max_y+1, min_x:max_x+1]
Image.fromarray(emblem_cropped).save("EAA_Emblem_Transparent.png")
print("Saved EAA_Emblem_Transparent.png:", emblem_cropped.shape)

# Save downscaled previews for verification
Image.fromarray(final_logo).resize((800, int(800 * final_logo.shape[0] / final_logo.shape[1])), Image.Resampling.LANCZOS).save("EAA_LOGO_Transparent_preview.png")
Image.fromarray(emblem_cropped).resize((800, int(800 * emblem_cropped.shape[0] / emblem_cropped.shape[1])), Image.Resampling.LANCZOS).save("EAA_Emblem_Transparent_preview.png")
