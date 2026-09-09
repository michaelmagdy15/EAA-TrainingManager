import shutil
from PIL import Image

logo = Image.open("EAA_LOGO_Transparent.png")
emblem = Image.open("EAA_Emblem_Transparent.png")

# Copy master files to Assets
logo.save("EAATrainingManager/Assets/EAA_LOGO_Transparent.png")
emblem.save("EAATrainingManager/Assets/EAA_Emblem_Transparent.png")

# Generate square app icons from the emblem
# Center emblem in a square canvas with padding
def make_square(img, size, pad=0.15):
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    target_w = int(size * (1.0 - 2 * pad))
    target_h = int(target_w * img.size[1] / img.size[0])
    if target_h > int(size * (1.0 - 2 * pad)):
        target_h = int(size * (1.0 - 2 * pad))
        target_w = int(target_h * img.size[0] / img.size[1])
    
    resized = img.resize((target_w, target_h), Image.Resampling.LANCZOS)
    offset_x = (size - target_w) // 2
    offset_y = (size - target_h) // 2
    canvas.paste(resized, (offset_x, offset_y), resized)
    return canvas

# Save Windows App SDK assets
make_square(emblem, 44, 0.08).save("EAATrainingManager/Assets/Square44x44Logo.scale-200.png")
make_square(emblem, 150, 0.12).save("EAATrainingManager/Assets/Square150x150Logo.scale-200.png")
make_square(emblem, 50, 0.08).save("EAATrainingManager/Assets/StoreLogo.png")
make_square(emblem, 48, 0.08).save("EAATrainingManager/Assets/Square44x44Logo.targetsize-48_altform-lightunplated.png")
make_square(emblem, 24, 0.08).save("EAATrainingManager/Assets/Square44x44Logo.targetsize-24_altform-unplated.png")

# Wide logo for splash / taskbar
splash = Image.new("RGBA", (620, 300), (0, 0, 0, 0))
target_w = 500
target_h = int(target_w * logo.size[1] / logo.size[0])
resized_logo = logo.resize((target_w, target_h), Image.Resampling.LANCZOS)
splash.paste(resized_logo, ((620 - target_w) // 2, (300 - target_h) // 2), resized_logo)
splash.save("EAATrainingManager/Assets/SplashScreen.scale-200.png")

# Wide310x150
wide = Image.new("RGBA", (310, 150), (0, 0, 0, 0))
tw = 260
th = int(tw * logo.size[1] / logo.size[0])
rl = logo.resize((tw, th), Image.Resampling.LANCZOS)
wide.paste(rl, ((310 - tw) // 2, (150 - th) // 2), rl)
wide.save("EAATrainingManager/Assets/Wide310x150Logo.scale-200.png")

# Save ICO
ico = make_square(emblem, 256, 0.05)
ico.save("EAATrainingManager/Assets/AppIcon.ico", format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

print("Successfully generated all EAA transparent branding assets in EAATrainingManager/Assets/!")
