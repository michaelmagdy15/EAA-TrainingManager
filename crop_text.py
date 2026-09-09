import cv2
import numpy as np
from PIL import Image

img = Image.open("EAA LOGO.jpg")
arr = np.array(img)

# Text region: Y from 800 to 1050, X from 450 to 2800
text_crop = arr[800:1050, 450:2800]
Image.fromarray(text_crop).save("text_crop.png")
print("Saved text_crop.png, size:", text_crop.shape)
