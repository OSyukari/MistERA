# ComfyUI nodes for the MistEra image classifier editor tool (workingdir/Plans/Plan_ImageClassifier.md).
# "WD14TaggerScoredPath": tags image files by path (one per line) and returns the scores as one JSON string,
#   both as the STRING output and in the ui payload ({"tags_json": [json]}), which /history carries back to the caller.
# "WD14TaggerScoredImage": tags an IMAGE batch and returns a JSON list in batch order through the same outputs.
# "MisteraFaceEmotion": face crop -> WD14 tags on the crop + one VLM label per image (ui {"emotion_json": [json]}).

import json
import os

import numpy as np
from PIL import Image

import folder_paths

from . import scored_tagger
# imported at startup on purpose: registers the CUDA 12 DLL folders before any node creates a llama.cpp model
# (ultralytics / llama_cpp themselves load lazily on first use)
from . import face_emotion


def _models_dir():
    if "wd14_tagger" in folder_paths.folder_names_and_paths:
        return folder_paths.get_folder_paths("wd14_tagger")[0]
    # the pysssss tagger's own model folder, so both nodes share the downloaded models
    return os.path.join(os.path.dirname(os.path.dirname(os.path.realpath(__file__))), "ComfyUI-WD14-Tagger", "models")


def _installed_models():
    directory = _models_dir()
    if not os.path.isdir(directory):
        return []
    return sorted(os.path.splitext(f)[0] for f in os.listdir(directory)
                  if f.endswith(".onnx") and os.path.isfile(os.path.join(directory, os.path.splitext(f)[0] + ".csv")))


def _split_paths(image_paths):
    return [p.strip() for p in image_paths.splitlines() if p.strip()]


class WD14TaggerScoredPath:
    @classmethod
    def INPUT_TYPES(cls):
        models = _installed_models() or ["wd-eva02-large-tagger-v3"]
        default = "wd-eva02-large-tagger-v3" if "wd-eva02-large-tagger-v3" in models else models[0]
        return {"required": {
            "image_paths": ("STRING", {"default": "", "multiline": True}),
            "model": (models, {"default": default}),
            "threshold": ("FLOAT", {"default": 0.2, "min": 0.0, "max": 1.0, "step": 0.01}),
            "character_threshold": ("FLOAT", {"default": 0.85, "min": 0.0, "max": 1.0, "step": 0.01}),
            "exclude_tags": ("STRING", {"default": ""}),
        }}

    RETURN_TYPES = ("STRING",)
    RETURN_NAMES = ("tags_json",)
    FUNCTION = "tag"
    OUTPUT_NODE = True
    CATEGORY = "image/MistEra"

    @classmethod
    def IS_CHANGED(cls, image_paths, **kwargs):
        # re-run when a file changes even though its path did not
        stamps = []
        for path in _split_paths(image_paths):
            try:
                st = os.stat(path)
                stamps.append(f"{path}|{st.st_size}|{st.st_mtime_ns}")
            except OSError:
                stamps.append(f"{path}|missing")
        return "\n".join(stamps)

    def tag(self, image_paths, model, threshold, character_threshold, exclude_tags=""):
        results = scored_tagger.tag_paths(_models_dir(), model, _split_paths(image_paths), threshold, character_threshold, exclude_tags)
        text = json.dumps(results, ensure_ascii=False)
        return {"ui": {"tags_json": [text]}, "result": (text,)}


class WD14TaggerScoredImage:
    @classmethod
    def INPUT_TYPES(cls):
        required = WD14TaggerScoredPath.INPUT_TYPES()["required"]
        required.pop("image_paths")
        return {"required": {"images": ("IMAGE",), **required}}

    RETURN_TYPES = ("STRING",)
    RETURN_NAMES = ("tags_json",)
    FUNCTION = "tag"
    OUTPUT_NODE = True
    CATEGORY = "image/MistEra"

    def tag(self, images, model, threshold, character_threshold, exclude_tags=""):
        results = []
        models_dir = _models_dir()
        for tensor in images:
            try:
                pixels = tensor.detach().cpu().float().numpy()
                image = Image.fromarray(np.clip(pixels * 255.0, 0, 255).astype(np.uint8)).convert("RGB")
                results.append(scored_tagger.tag_image(models_dir, model, image, threshold, character_threshold, exclude_tags))
            except Exception as e:
                results.append({"error": f"{type(e).__name__}: {e}"})
        text = json.dumps(results, ensure_ascii=False)
        return {"ui": {"tags_json": [text]}, "result": (text,)}


class MisteraFaceEmotion:
    """Face crop -> WD14 tags on the crop + one VLM label, for portrait images (hybrid emotion pass).
    Model inputs are paths relative to ComfyUI's models folder (absolute paths also work)."""

    @classmethod
    def INPUT_TYPES(cls):
        models = _installed_models() or ["wd-eva02-large-tagger-v3"]
        default = "wd-eva02-large-tagger-v3" if "wd-eva02-large-tagger-v3" in models else models[0]
        return {"required": {
            "image_paths": ("STRING", {"default": "", "multiline": True}),
            "face_model": ("STRING", {"default": "ultralytics/segm/99coins_anime_girl_face_m_seg.pt"}),
            "face_padding": ("FLOAT", {"default": 0.35, "min": 0.0, "max": 2.0, "step": 0.05}),
            "tagger_model": (models, {"default": default}),
            "tag_threshold": ("FLOAT", {"default": 0.2, "min": 0.0, "max": 1.0, "step": 0.01}),
            "vlm_model": ("STRING", {"default": "clip/Qwen2.5-VL-7B-Instruct-abliterated.Q4_K_M.gguf"}),
            "vlm_mmproj": ("STRING", {"default": "clip/Qwen2.5-VL-7B-Instruct-abliterated.mmproj-f16.gguf"}),
            "labels": ("STRING", {"default": "happy, relaxed, confident, shy, surprised, thinking, serious, sleeping, resting"}),
            "prompt": ("STRING", {"default": "Look at this anime character's face. Which ONE of these expressions fits best? {labels}. "
                                             "Answer with exactly one word from the list, nothing else.", "multiline": True}),
        }}

    RETURN_TYPES = ("STRING",)
    RETURN_NAMES = ("emotion_json",)
    FUNCTION = "run"
    OUTPUT_NODE = True
    CATEGORY = "image/MistEra"

    @classmethod
    def IS_CHANGED(cls, image_paths, **kwargs):
        return WD14TaggerScoredPath.IS_CHANGED(image_paths) + "\n" + json.dumps(kwargs, sort_keys=True, default=str)

    def run(self, image_paths, face_model, face_padding, tagger_model, tag_threshold, vlm_model, vlm_mmproj, labels, prompt):
        def model_path(p):
            return p if os.path.isabs(p) else os.path.join(folder_paths.models_dir, p)

        label_list = [l.strip().lower() for l in labels.split(",") if l.strip()]
        results = face_emotion.face_emotion_paths(_split_paths(image_paths), model_path(face_model), _models_dir(), tagger_model,
                                                  model_path(vlm_model), model_path(vlm_mmproj), label_list, prompt, face_padding, tag_threshold)
        text = json.dumps(results, ensure_ascii=False)
        return {"ui": {"emotion_json": [text]}, "result": (text,)}


NODE_CLASS_MAPPINGS = {
    "WD14TaggerScoredPath": WD14TaggerScoredPath,
    "WD14TaggerScoredImage": WD14TaggerScoredImage,
    "MisteraFaceEmotion": MisteraFaceEmotion,
}
NODE_DISPLAY_NAME_MAPPINGS = {
    "WD14TaggerScoredPath": "WD14 Tagger Scored (paths) - MistEra",
    "WD14TaggerScoredImage": "WD14 Tagger Scored (image) - MistEra",
    "MisteraFaceEmotion": "Face Emotion (crop + WD14 + VLM, paths) - MistEra",
}
