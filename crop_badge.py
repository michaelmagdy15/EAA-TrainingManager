import cv2
import numpy as np
from PIL import Image

img = Image.open("EAA LOGO.jpg")
arr = np.array(img)

# Center region: Y from 250 to 750, X from 1250 to 2000
badge_crop = arr[250:750, 1250:2000]
Image.fromarray(badge_crop).save("badge_crop.png")
print("Saved badge_crop.png, size:", badge_crop.shape)
