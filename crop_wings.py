import cv2
import numpy as np
from PIL import Image

img = Image.open("EAA LOGO.jpg")
arr = np.array(img)

# Wings region: Y from 380 to 720, X from 280 to 2980
wings_crop = arr[380:720, 280:2980]
Image.fromarray(wings_crop).save("wings_crop.png")
print("Saved wings_crop.png, size:", wings_crop.shape)
