import cv2
import numpy as np
from PIL import Image

# Load original high-res EAA LOGO.jpg
img = Image.open("EAA LOGO.jpg").convert("RGB")
arr = np.array(img)
h, w, _ = arr.shape

# RGBA output canvas
out_rgba = np.zeros((h, w, 4), dtype=np.uint8)
out_rgba[:, :, :3] = arr

# Color channels in float
rgb = arr.astype(float)
r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
luma = 0.299 * r + 0.587 * g + 0.114 * b
saturation = np.maximum(np.maximum(r, g), b) - np.minimum(np.minimum(r, g), b)

# Alpha mask (0.0 to 1.0)
alpha = np.zeros((h, w), dtype=float)

# =========================================================================
# 1. CENTRAL OVAL BADGE
# =========================================================================
# Ellipse params
cx, cy = 1635.3, 543.8
rx, ry = 307.0, 243.0  # Radii

# Compute normalized distance from center
y_coords, x_coords = np.ogrid[:h, :w]
ellipse_dist = ((x_coords - cx) / rx) ** 2 + ((y_coords - cy) / ry) ** 2

# Anti-aliased edge over 2 pixels
badge_mask = np.clip(1.0 - (ellipse_dist - 0.98) / 0.04, 0.0, 1.0)
alpha = np.maximum(alpha, badge_mask)

# =========================================================================
# 2. BLUE WINGS (Left & Right)
# =========================================================================
# Wings region
wings_region = (y_coords >= 380) & (y_coords <= 750) & (x_coords >= 280) & (x_coords <= 2980)

# Detect blue/cyan pixels: either high blue diff, or high saturation with blue dominant
is_blue = (b > r + 12) | ((b > g + 5) & (b > 80)) | ((saturation > 18) & (b > 90) & (r < 180))

# Also detect the dark beveled shadow edges of the wings
is_wing_shadow = (luma < 145) & wings_region & ((x_coords < 1350) | (x_coords > 1920))

# Combine for wing core
wing_core = (is_blue | is_wing_shadow) & wings_region

# Use morphological closing to bridge internal metallic specular highlights inside the wings
kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7))
wing_mask_uint = (wing_core * 255).astype(np.uint8)
wing_closed = cv2.morphologyEx(wing_mask_uint, cv2.MORPH_CLOSE, kernel)

# Smooth edges with Gaussian Blur
wing_smooth = cv2.GaussianBlur(wing_closed.astype(float) / 255.0, (5, 5), 1.0)
alpha = np.maximum(alpha, wing_smooth)

# =========================================================================
# 3. TEXT: "Egyptian Aviation Academy"
# =========================================================================
# Text region
text_region = (y_coords >= 780) & (y_coords <= 1060) & (x_coords >= 450) & (x_coords <= 2800)

# The plate background luma in this region is ~205-225
# Text letters have luma < 165
# Compute smooth anti-aliased font alpha
bg_luma = 212.0
text_alpha_raw = np.clip((bg_luma - luma) / (bg_luma - 50.0), 0.0, 1.0)

# Clean up noise outside letters
text_mask_binary = (luma < 160) & text_region & ((saturation > 8) | (luma < 110))
text_mask_uint = (text_mask_binary * 255).astype(np.uint8)

# Remove tiny isolated noise specks (< 30px)
num_labels, labels, stats, _ = cv2.connectedComponentsWithStats(text_mask_uint)
for i in range(1, num_labels):
    if stats[i, cv2.CC_STAT_AREA] < 30:
        text_mask_uint[labels == i] = 0

# Dilate slightly to capture anti-aliased perimeter of serif letters
text_dilated = cv2.dilate(text_mask_uint, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3)))
text_smooth = (text_dilated.astype(float) / 255.0) * text_alpha_raw

alpha = np.maximum(alpha, text_smooth)

# Set final alpha channel
out_rgba[:, :, 3] = np.clip(alpha * 255.0, 0, 255).astype(np.uint8)

# Crop transparent borders to content bounding box
non_zero = np.where(out_rgba[:, :, 3] > 10)
if len(non_zero[0]) > 0 and len(non_zero[1]) > 0:
    min_y, max_y = non_zero[0].min(), non_zero[0].max()
    min_x, max_x = non_zero[1].min(), non_zero[1].max()
    
    # Add 20px padding
    pad = 20
    min_y = max(0, min_y - pad)
    max_y = min(h - 1, max_y + pad)
    min_x = max(0, min_x - pad)
    max_x = min(w - 1, max_x + pad)
    
    cropped_logo = out_rgba[min_y:max_y+1, min_x:max_x+1]
else:
    cropped_logo = out_rgba

# Save transparent PNG
Image.fromarray(cropped_logo).save("EAA_LOGO_Transparent.png")
print("Saved EAA_LOGO_Transparent.png, dimensions:", cropped_logo.shape)

# Also save a downscaled preview to check visually
thumb = Image.fromarray(cropped_logo)
thumb.thumbnail((800, 400), Image.Resampling.LANCZOS)
thumb.save("EAA_LOGO_Transparent_preview.png")
print("Saved EAA_LOGO_Transparent_preview.png")
