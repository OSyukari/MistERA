# MistEra Scored WD14 Nodes

## Installation

Copy this entire `mistera_wd14_scored` package to
`G:\ComfyUI_windows_portable\ComfyUI\custom_nodes\mistera_wd14_scored`, then restart ComfyUI.
Changes in this workspace do not update the live installation automatically.

The package uses the existing ComfyUI Python environment, including NumPy, Pillow,
and ONNX Runtime. WD14 models require matching `.onnx` and `.csv` files. Nodes use
the registered `wd14_tagger` model directory when available, otherwise
`custom_nodes/ComfyUI-WD14-Tagger/models`. Existing face-emotion dependencies are
unchanged.

## Generation Workflow

Add **WD14 Tagger Scored (image) - MistEra** (`WD14TaggerScoredImage`) from
`image/MistEra`. Connect its `images` input to the same IMAGE batch as the
`MisteraOutput` node. Preserve the same batch order; do not insert a reorder,
filter, or separate image source on either branch. This is an output node, so its
STRING output does not need another connection for it to execute.

The model, threshold, character threshold, and excluded-tag settings match
`WD14TaggerScoredPath`. Defaults are `wd-eva02-large-tagger-v3` when installed
(otherwise the first installed model), `0.2`, `0.85`, and no excluded tags.

Each batch tensor image is converted from float RGB values in the range 0-1 to
an 8-bit PIL RGB image. Values are clipped to 0-255 and truncated to integers.
The existing scorer performs resizing and inference.

## Output Contract

The STRING output `tags_json` contains a JSON **list**, one entry per image in
input batch order. The same string appears in the node's history output at
`tags_json[0]`, produced by `{"ui": {"tags_json": [text]}}`.

```json
[
  {
    "rating": {"general": 0.98},
    "general": {"smile": 0.91},
    "character": {}
  }
]
```

An individual image failure produces `{"error": "ExceptionType: message"}` in
that image's position without dropping later images. An empty batch returns
`[]`. Consumers must treat error entries as unavailable tag records.

The game identifies the node by graph `class_type` `WD14TaggerScoredImage` and
matches list positions to the `MisteraOutput` batch. Under the planned game-side
integration, missing or unmatched tag records use scene data alone for naming;
the workflow does not need a separate tagging request.

## Existing Nodes

- `WD14TaggerScoredPath`: tags newline-separated file paths and returns a JSON
  object keyed by path, not a batch-position list.
- `MisteraFaceEmotion`: performs the existing file-path face-emotion pass and
  returns `emotion_json`.
