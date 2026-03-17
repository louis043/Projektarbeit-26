import sys, json
from pathlib import Path
from PIL import Image
from transformers import BlipProcessor, BlipForConditionalGeneration
from transformers.utils import logging
logging.set_verbosity_error()

if len(sys.argv) < 3:
    print("Usage: python caption.py <image_path> <output_json>")
    sys.exit(1)

image_path = Path(sys.argv[1])
output_path = Path(sys.argv[2])

processor = BlipProcessor.from_pretrained("Salesforce/blip-image-captioning-base")
model = BlipForConditionalGeneration.from_pretrained("Salesforce/blip-image-captioning-base")

image = Image.open(image_path).convert("RGB")
inputs = processor(image, return_tensors="pt")

output = model.generate(**inputs, max_length=80, num_beams=5)
caption = processor.decode(output[0], skip_special_tokens=True)

result = {
    "image_path": str(image_path),
    "caption": caption
}

output_path.parent.mkdir(parents=True, exist_ok=True)
output_path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")

print("Caption:", caption)
