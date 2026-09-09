import cv2
import numpy as np
from PIL import Image

badge_crop = cv2.imread("badge_crop.png")
gray = cv2.cvtColor(badge_crop, cv2.COLOR_BGR2GRAY)

# The badge has a dark beveled edge / shadow
# Let's find edges or threshold to fit ellipse
# The outer rim has a strong gradient edge
edges = cv2.Canny(gray, 50, 150)

# Find contours
contours, _ = cv2.findContours(edges, cv2.RETR_TREE, cv2.CHAIN_APPROX_SIMPLE)
best_ellipse = None
max_area = 0

for c in contours:
    if len(c) >= 5:
        ellipse = cv2.fitEllipse(c)
        (cx, cy), (d1, d2), angle = ellipse
        area = np.pi * (d1 / 2) * (d2 / 2)
        # We expect badge to be large inside the 750x500 crop
        if 200 < d1 < 700 and 200 < d2 < 700 and area > max_area and 0.6 < min(d1, d2)/max(d1, d2) < 0.9:
            max_area = area
            best_ellipse = ellipse

if best_ellipse:
    (cx, cy), (d1, d2), angle = best_ellipse
    print(f"Fitted ellipse in crop: Center=({cx:.1f}, {cy:.1f}), Axes=({d1:.1f}, {d2:.1f}), Angle={angle:.1f}")
    # Map back to full image (Y+250, X+1250)
    full_cx = cx + 1250
    full_cy = cy + 250
    print(f"Full image Ellipse: Center=({full_cx:.1f}, {full_cy:.1f}), Axes=({d1:.1f}, {d2:.1f}), Angle={angle:.1f}")
