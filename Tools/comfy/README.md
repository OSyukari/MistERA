# ComfyUI node for the image classifier tool

`mistera_wd14_scored/` is the versioned copy of the ComfyUI custom node used by the image classifier editor tool
(`Docs/ImagePortraits/Plan_ImageClassifier.md`). The live copy is in `G:\ComfyUI_windows_portable\ComfyUI\custom_nodes\mistera_wd14_scored\`.
After changing it, copy it there and restart ComfyUI.

Node `WD14TaggerScoredPath` (category `image/MistEra`):
- `image_paths`: absolute image file paths, one per line (a batch per request).
- `model`, `threshold` (default 0.2), `character_threshold` (default 0.85), `exclude_tags` (comma separated).
- Output `tags_json`, also returned in the ui payload as `{"tags_json": ["<json>"]}`:
  `{"<path>": {"rating": {...}, "general": {"tag": 0.9731, ...}, "character": {...}}}`, each map sorted by score.
  A file that fails gets `{"<path>": {"error": "..."}}`; the rest of the batch still runs.

It reuses the models of ComfyUI-WD14-Tagger (`custom_nodes/ComfyUI-WD14-Tagger/models/*.onnx` + `.csv`) and needs no extra packages.
