# WD14 tagging with confidence scores, for the MistEra image classifier editor tool.
# Same preprocessing and models as ComfyUI-WD14-Tagger (pysssss), but keeps the scores, sorts by score,
# does not escape parentheses, keeps underscores, caches one InferenceSession per model, and reads image files by path.
# No ComfyUI imports here, so this module can be tested with plain python.

import csv
import os
import threading

import numpy as np
import onnxruntime as ort
from PIL import Image

RATING_CATEGORY = "9"
CHARACTER_CATEGORY = "4"

_lock = threading.Lock()
_models = {}  # model path without extension -> (session, names, categories)


_dlls_preloaded = False


def _preload_cuda_dlls():
    # onnxruntime's CUDA provider needs cuDNN; inside ComfyUI torch has usually loaded it already.
    # preload_dlls (onnxruntime >= 1.21) finds the copies shipped with torch / nvidia pip packages.
    global _dlls_preloaded
    if _dlls_preloaded:
        return
    _dlls_preloaded = True
    try:
        import torch  # noqa: F401  loads torch/lib cuDNN on Windows
    except Exception:
        pass
    if hasattr(ort, "preload_dlls"):
        try:
            ort.preload_dlls()
        except Exception:
            pass


def _providers():
    available = ort.get_available_providers()
    wanted = [p for p in ("CUDAExecutionProvider", "CPUExecutionProvider") if p in available]
    return wanted or available


def _create_session(model_path):
    """CUDA session if it can actually run (checked with one dummy inference), otherwise CPU."""
    _preload_cuda_dlls()
    providers = _providers()
    session = ort.InferenceSession(model_path, providers=providers)
    if "CUDAExecutionProvider" not in session.get_providers():
        return session
    model_input = session.get_inputs()[0]
    size = model_input.shape[1]
    try:
        session.run([session.get_outputs()[0].name], {model_input.name: np.zeros((1, size, size, 3), np.float32)})
        return session
    except Exception as e:
        print(f"[mistera_wd14_scored] CUDA unusable, falling back to CPU: {e}")
        return ort.InferenceSession(model_path, providers=["CPUExecutionProvider"])


def load_model(models_dir, model_name):
    if model_name.endswith(".onnx"):
        model_name = model_name[:-5]
    base = os.path.join(models_dir, model_name)
    with _lock:
        cached = _models.get(base)
        if cached is not None:
            return cached
        if not os.path.isfile(base + ".onnx") or not os.path.isfile(base + ".csv"):
            raise FileNotFoundError(f"model files not found: {base}.onnx / {base}.csv")
        names, categories = [], []
        with open(base + ".csv", encoding="utf-8") as f:
            reader = csv.reader(f)
            next(reader)
            for row in reader:
                names.append(row[1])
                categories.append(row[2])
        session = _create_session(base + ".onnx")
        cached = (session, names, categories)
        _models[base] = cached
        return cached


def open_image_rgb(path):
    image = Image.open(path)
    image.seek(0)  # first frame of animated webp/gif
    if image.mode in ("RGBA", "LA", "P"):
        image = image.convert("RGBA")
        background = Image.new("RGB", image.size, (255, 255, 255))
        background.paste(image, mask=image.getchannel("A"))
        return background
    return image.convert("RGB")


def _prepare(image, size):
    # same as pysssss: fit into a white square, BGR, float32 0-255
    ratio = float(size) / max(image.size)
    new_size = tuple(max(1, int(x * ratio)) for x in image.size)
    image = image.resize(new_size, Image.LANCZOS)
    square = Image.new("RGB", (size, size), (255, 255, 255))
    square.paste(image, ((size - new_size[0]) // 2, (size - new_size[1]) // 2))
    array = np.array(square).astype(np.float32)[:, :, ::-1]
    return np.expand_dims(array, 0)


def tag_image(models_dir, model_name, image, threshold=0.2, character_threshold=0.85, exclude_tags=""):
    """Returns {"rating": {...}, "general": {...}, "character": {...}}, each sorted by score, scores rounded to 4 places.
    Rating holds all rating classes; general/character hold only tags above their threshold."""
    session, names, categories = load_model(models_dir, model_name)
    model_input = session.get_inputs()[0]
    size = model_input.shape[1]
    probs = session.run([session.get_outputs()[0].name], {model_input.name: _prepare(image, size)})[0][0]

    exclude = {t.strip() for t in exclude_tags.split(",") if t.strip()}
    rating, general, character = [], [], []
    for name, category, prob in zip(names, categories, probs):
        prob = float(prob)
        if category == RATING_CATEGORY:
            rating.append((name, prob))
        elif name in exclude:
            continue
        elif category == CHARACTER_CATEGORY:
            if prob > character_threshold:
                character.append((name, prob))
        elif prob > threshold:
            general.append((name, prob))

    def ordered(items):
        return {name: round(prob, 4) for name, prob in sorted(items, key=lambda x: -x[1])}

    return {"rating": ordered(rating), "general": ordered(general), "character": ordered(character)}


def tag_paths(models_dir, model_name, paths, threshold=0.2, character_threshold=0.85, exclude_tags=""):
    """Tags every path; a failing path gets {"error": "..."} instead of failing the batch. Keeps input order."""
    results = {}
    for path in paths:
        try:
            results[path] = tag_image(models_dir, model_name, open_image_rgb(path), threshold, character_threshold, exclude_tags)
        except Exception as e:
            results[path] = {"error": f"{type(e).__name__}: {e}"}
    return results
