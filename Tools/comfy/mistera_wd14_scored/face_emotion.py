# Face-crop emotion data for the MistEra image classifier editor tool (Plan_ImageClassifier.md, hybrid emotion pass):
# per image: detect the face (ultralytics anime face model), crop it with padding, run WD14 (scored_tagger) on the crop,
# and ask a Qwen2.5-VL GGUF (llama-cpp-python) to pick one label. No ComfyUI imports here, so it can be tested with plain python.

import base64
import io
import os
import threading

# llama.cpp's CUDA backend in this ComfyUI is built for CUDA 12 while torch ships CUDA 13: register the
# nvidia-cuda-runtime-cu12 / nvidia-cublas-cu12 pip packages before llama.cpp loads its backends (first Llama()).
_nv = os.path.join(os.path.dirname(os.__file__), "site-packages", "nvidia")
_cuda12 = [os.path.join(_nv, "cuda_runtime", "bin"), os.path.join(_nv, "cublas", "bin")]
for _d in _cuda12:
    if os.path.isdir(_d):
        os.add_dll_directory(_d)
        os.environ["PATH"] = _d + os.pathsep + os.environ.get("PATH", "")

from PIL import Image

from . import scored_tagger

_lock = threading.Lock()
_face_models = {}
_vlms = {}


def _face_model(path):
    with _lock:
        if path not in _face_models:
            from ultralytics import YOLO
            _face_models[path] = YOLO(path)
        return _face_models[path]


def _vlm(model_path, mmproj_path):
    key = (model_path, mmproj_path)
    with _lock:
        if key not in _vlms:
            from llama_cpp import Llama
            from llama_cpp.llama_chat_format import Qwen25VLChatHandler
            _vlms[key] = Llama(model_path=model_path, chat_handler=Qwen25VLChatHandler(clip_model_path=mmproj_path, verbose=False),
                               n_ctx=4096, n_gpu_layers=-1, verbose=False)
        return _vlms[key]


def crop_face(image, face_model_path, padding=0.35, confidence=0.3):
    """Largest detected face, padded on every side; None when no face is found."""
    result = _face_model(face_model_path).predict(image, verbose=False, conf=confidence)[0]
    if result.boxes is None or len(result.boxes) == 0:
        return None, None
    x1, y1, x2, y2 = max(result.boxes.xyxy.tolist(), key=lambda b: (b[2] - b[0]) * (b[3] - b[1]))
    w, h = x2 - x1, y2 - y1
    box = (max(0, x1 - w * padding), max(0, y1 - h * padding), min(image.width, x2 + w * padding), min(image.height, y2 + h * padding))
    return image.crop(box), [round(v) for v in box]


def ask_label(llm, image, labels, prompt_template, max_side=448):
    """One label from the list (temperature 0); returns (label or None, raw answer)."""
    img = image.copy()
    img.thumbnail((max_side, max_side))
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    uri = "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode()
    prompt = prompt_template.replace("{labels}", ", ".join(labels))
    r = llm.create_chat_completion(messages=[{"role": "user", "content": [{"type": "image_url", "image_url": {"url": uri}},
                                                                          {"type": "text", "text": prompt}]}],
                                   temperature=0.0, max_tokens=8)
    raw = r["choices"][0]["message"]["content"].strip()
    answer = raw.lower().strip(" .!\"'")
    if answer in labels:
        return answer, raw
    for label in labels:  # e.g. "Happy." or "happy expression"
        if answer.startswith(label):
            return label, raw
    return None, raw


def face_emotion_paths(paths, face_model_path, tagger_models_dir, tagger_model, vlm_model_path, vlm_mmproj_path,
                       labels, prompt_template, padding=0.35, tag_threshold=0.2):
    """{path: {"face": bool, "box": [...], "faceTags": {...}, "vlm": label|None, "vlmRaw": str}} or {path: {"error": ...}}."""
    llm = _vlm(vlm_model_path, vlm_mmproj_path)
    results = {}
    for path in paths:
        try:
            image = scored_tagger.open_image_rgb(path)
            crop, box = crop_face(image, face_model_path, padding)
            if crop is None:
                results[path] = {"face": False}
                continue
            tags = scored_tagger.tag_image(tagger_models_dir, tagger_model, crop, threshold=tag_threshold)["general"]
            label, raw = ask_label(llm, crop, labels, prompt_template)
            results[path] = {"face": True, "box": box, "faceTags": tags, "vlm": label, "vlmRaw": raw}
        except Exception as e:
            results[path] = {"error": f"{type(e).__name__}: {e}"}
    return results
