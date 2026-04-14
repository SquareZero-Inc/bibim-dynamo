"""
BIBIM Icon Resizer
Resizes icon files to multiple sizes and creates ICO file
"""
from PIL import Image
import os

# Paths
white_icon = "Assets/Icons/bibim-logo-white-512.png"
blue_icon = "Assets/Icons/bibim-logo-blue-512.png"
output_dir = "Assets/Icons"

# Sizes to generate
sizes = [16, 24, 32, 48, 64, 128, 256]

def resize_icon(input_path, output_prefix, sizes):
    """Resize icon to multiple sizes"""
    img = Image.open(input_path)
    
    # Ensure RGBA mode for transparency
    if img.mode != 'RGBA':
        img = img.convert('RGBA')
    
    resized_images = []
    
    for size in sizes:
        resized = img.resize((size, size), Image.Resampling.LANCZOS)
        output_path = f"{output_dir}/{output_prefix}-{size}.png"
        resized.save(output_path, 'PNG')
        print(f"Created: {output_path}")
        resized_images.append(resized)
    
    return resized_images

def create_ico(images, output_path):
    """Create multi-size ICO file"""
    if images:
        images[0].save(output_path, format='ICO', sizes=[(img.width, img.height) for img in images])
        print(f"Created ICO: {output_path}")

if __name__ == "__main__":
    print("Resizing white icons...")
    white_images = resize_icon(white_icon, "bibim-logo-white", sizes)
    
    print("\nResizing blue icons...")
    blue_images = resize_icon(blue_icon, "bibim-logo-blue", sizes)
    
    print("\nCreating ICO files...")
    create_ico(white_images, f"{output_dir}/bibim-icon-white.ico")
    create_ico(blue_images, f"{output_dir}/bibim-icon-blue.ico")
    
    print("\n✅ All icons generated successfully!")
