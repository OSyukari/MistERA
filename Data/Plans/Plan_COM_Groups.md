# Plan: COM Groups (Parent-Child Command Grouping)

## Goal
Group similar COMs under a single parent button in the UI.
- UI shows one expandable parent button per group
- Clicking opens a submenu listing valid child COMs
- Children inherit the parent's acceptance/difficulty computed result and add their own delta
- LLM output prints one group entry with a concise child list, not N repeated entries

Examples:
- **Watch TV** → [Watch Program A, Watch Program B]
- **Get a meal** → [Eat Onigiri, Eat Instant Ramen]
- **Physical contact** → [Touch Hand, Touch Hair]

---

## Current System (relevant parts)

| Concept | File | Detail |
|---|---|---|
| `COM` | `COM.cs` | Has `AcceptanceCheck`, `DifficultyCheck`, `requirements`, `variants`, `results`, `comTags` |
| `COM.Acceptance` | `COM.cs:226` | `baseAcceptanceValue`, `useDefault`, `SkillBonus_Doer/Receiver` |
| `COM.Difficulty` | `COM.cs:233` | `baseD20Check`, `useDefault`, `SkillBonus_*`, `moodMod`, `stressMod`, `lustMod` |
| `MakeCOMButton()` | `scr_panel_COMmanager.cs:1155` | Instantiates `prefab_COMbutton`, initializes `ButtonValidator_validateCOM`, registers in `indexCOM` |
| `LLMUtils.CollectCOMInfo()` | `LLMUtils.cs:1027` | Iterates jobs/packages, calls `validateSingle()` per target, builds `Dictionary<string, List<SerializedAP>>` |

There is currently no grouping concept in COM. Every COM maps to one button.

---

## Design

### Core concept: Parent-Child COM relationship

Two new fields on `COM`:

```csharp
// If set, this COM is a GROUP PARENT.
// The UI renders it as an expandable group button.
// Children are listed inside the group when the button is clicked.
public List<string> childCOMIDs = null;

// If set, this COM is a CHILD of the named group parent.
// The child does not appear as a standalone button.
// Acceptance and Difficulty use parent's computed result as base.
public string parentCOMID = "";
```

**A COM is a group parent** when `childCOMIDs != null && childCOMIDs.Count > 0`.
**A COM is a child** when `parentCOMID != ""`.

These are mutually exclusive in normal use (a child should not itself be a group parent).

---

### Two delta fields on `COM.Acceptance` and `COM.Difficulty`

Instead of children re-running the full acceptance/difficulty pipeline, they add an offset to the
parent's result:

```csharp
public class Acceptance
{
    public int baseAcceptanceValue = 0;
    public bool useDefault = true;
    public List<string> SkillBonus_Doer = ...;
    public List<string> SkillBonus_Receiver = ...;

    // NEW: when this COM is a child, this is added to the parent's computed acceptance result
    public int parentMod = 0;
}

public class Difficulty
{
    public int baseD20Check = 0;
    public bool useDefault = true;
    public List<string> SkillBonus_Doer = ...;
    public List<string> SkillBonus_Receiver = ...;
    public int moodMod = 0;
    public int stressMod = 0;
    public int lustMod = 0;

    // NEW: when this COM is a child, this is added to the parent's computed DC result
    public int parentMod = 0;
}
```

Default `parentMod = 0` means neutral delta (child inherits parent's score exactly).

---

### How "inheriting validation" works

When computing acceptance or DC for a child COM:

**Step 1 — Look up parent COM:**
```
parentCOM = Index_COM.GetByID(child.parentCOMID)
```

**Step 2 — Compute parent's full score** (same pipeline as before, no changes):
```
parentAccScore = parentCOM.AcceptanceCheck.ComputeScore(doers, receivers, faction)
parentDCScore  = parentCOM.DifficultyCheck.ComputeScore(doers, receivers, faction)
```

**Step 3 — Child's effective score = parent score + child's delta:**
```
childAccScore = parentAccScore + child.AcceptanceCheck.parentMod
childDCScore  = parentDCScore  + child.DifficultyCheck.parentMod
```

**Step 4 — Child's own requirements** (item check, faction check, etc.) still run normally
(these are the DIFFERENTIATORS between children, e.g. "requires item X in faction inventory").

**No changes to variant selection logic.** Each child COM has its own variants and its own
`GetValidVariant()` call as before.

> **Key point**: Children do NOT re-run the parent's character/count requirements. Those are
> guaranteed-valid at the point the submenu is opened. Children only check their own additional
> requirements (the delta conditions that distinguish them from each other).

---

## Three Touch Points

### 1. Data Layer — COM.cs

**Add to `COM`:**
```csharp
public List<string> childCOMIDs = null;
public string parentCOMID = "";
[JsonIgnore] public bool IsGroupParent => childCOMIDs != null && childCOMIDs.Count > 0;
[JsonIgnore] public bool IsGroupChild => parentCOMID != "";
```

**Add to `COM.Acceptance` and `COM.Difficulty`:**
```csharp
public int parentMod = 0;
```

**Add a helper on COM:**
```csharp
// Returns the parent COM if this is a child, else null
[JsonIgnore] public COM ParentCOM => parentCOMID == "" ? null : scr_System_Serializer.current.MasterList.COMs.GetByID(parentCOMID);
```

**Add a helper on COM to compute effective base acceptance score (for children):**
```csharp
public int GetEffectiveBaseAcceptance(List<Character_Trainable> doers, List<Character_Trainable> receivers)
{
    if (!IsGroupChild || ParentCOM == null) return AcceptanceCheck.baseAcceptanceValue;
    int parentScore = ParentCOM.GetEffectiveBaseAcceptance(doers, receivers); // parent's own base
    return parentScore + AcceptanceCheck.parentMod;
}

public int GetEffectiveBaseDC(List<Character_Trainable> doers, List<Character_Trainable> receivers)
{
    if (!IsGroupChild || ParentCOM == null) return DifficultyCheck.baseD20Check;
    int parentScore = ParentCOM.GetEffectiveBaseDC(doers, receivers);
    return parentScore + DifficultyCheck.parentMod;
}
```

Existing code that reads `com.baseAcceptanceValue` and `com.baseD20Check` is replaced with
calls to these helpers where child behavior matters.

---

### 2. UI Layer — scr_panel_COMmanager.cs

**During ValidateAll / button printing:**

Current:
```
for each COM in list → MakeCOMButton(COM)
```

New:
```
for each COM in list:
    if COM.IsGroupChild → SKIP (children appear only inside the group submenu)
    else if COM.IsGroupParent → MakeCOMGroupButton(COM)
    else → MakeCOMButton(COM)   // unchanged for non-grouped COMs
```

**`MakeCOMGroupButton(COM parent)`:**
- Instantiates a new group button prefab (`prefab_COMGroupButton`, to be created)
- Validates using the PARENT COM's requirements (same logic as current `ButtonValidator_validateCOM`)
- On click → opens a submenu panel listing each child COM
- The submenu validates each child COM independently (its own variant + extra requirements)
- Each child in the submenu uses current `MakeCOMButton` logic, but with the inherited acceptance/DC

**Group button visual:**
- Same text style as regular button but with a `▶` or `+` indicator
- When expanded (clicked), shows child list below or in an overlay panel
- Child buttons show their own `displayName` (not the parent's)

**Group button validation:**
- The parent button is valid if the parent COM's `GetValidVariant()` passes
- The child buttons in the submenu individually validate child COM requirements
- A child button can be grayed/hidden if its extra requirement fails (e.g., item not in inventory)
- If ALL children are invalid, the group button itself is grayed

---

### 3. LLM Output — LLMUtils.cs

In `CollectCOMInfo()` / `validateSingle()`:

**Current output per COM:**
```
"Watch TV"     → { acceptance: 5, dc: 8, ... }
"Watch Prog A" → { acceptance: 7, dc: 11, ... }
"Watch Prog B" → { acceptance: 5, dc: 8, ... }
```

**New output for group:**
```
"Watch TV (group)":
  - Watch Program A [+2 acc, +3 dc, requires: item_tvguide_programA]
  - Watch Program B [±0 acc, ±0 dc]
```

Implementation:
- When iterating COMs for LLM: skip child COMs
- For group parent COMs: after printing parent info, iterate `childCOMIDs`, look up each child COM,
  validate child's extra requirements, and print a compact child line with `parentMod` values
- Child line format: `"$displayName$ [acc $sign$$mod$, dc $sign$$mod$]"` plus any extra conditions as text

---

## JSON Authoring

### Group parent
```json
{
  "ID": "com_daily_watch_tv",
  "displayName": "Watch TV",
  "childCOMIDs": ["com_daily_watch_programA", "com_daily_watch_programB"],
  "AcceptanceCheck": { "baseAcceptanceValue": 5 },
  "DifficultyCheck": { "baseD20Check": 8 },
  "requirements": {
    "requirement": { "doerCount": 1, "req_Doers": { "allowNPC": false } }
  },
  "variants": [
    { "requirements": { "requirement": { "doerCount": 1 } }, "displayName": "Watch TV" }
  ]
}
```

### Child COM
```json
{
  "ID": "com_daily_watch_programA",
  "displayName": "Watch Program A",
  "parentCOMID": "com_daily_watch_tv",
  "AcceptanceCheck": { "parentMod": 2 },
  "DifficultyCheck": { "parentMod": 3 },
  "requirements": {
    "requireFactionExisting": { "inventoryItemBaseID": "item_tvguide_programA" }
  },
  "variants": [
    { "displayName": "Watch Program A" }
  ],
  "results": {
    "results_character": [
      { "entry_conditions": { "applyToDoer": true }, "entry_results": { "type": "statMod_EN", "value": "1" } }
    ]
  },
  "description_begin": {
    "Entries": [ { "keepLooking": false, "texts": ["com_daily_watch_programA_begin_1"] } ]
  }
}
```

### Child with no delta (plain grouping, shares parent's score exactly)
```json
{
  "ID": "com_daily_watch_programB",
  "displayName": "Watch Program B",
  "parentCOMID": "com_daily_watch_tv",
  "variants": [ { "displayName": "Watch Program B" } ],
  "description_begin": {
    "Entries": [ { "keepLooking": false, "texts": ["com_daily_watch_programB_begin_1"] } ]
  }
}
```

### Non-item-gated grouping (physical contact example)
Children here are distinguished only by their target, not by faction items:
```json
[
  { "ID": "com_touch_group", "displayName": "Physical Contact",
    "childCOMIDs": ["com_touch_hand", "com_touch_hair"],
    "AcceptanceCheck": { "baseAcceptanceValue": 3 },
    "DifficultyCheck": { "baseD20Check": 6 },
    "requirements": { "requirement": { "doerCount": 1, "receiverCount": 1 } },
    "variants": [ { "requirements": { "requirement": { "doerCount": 1, "receiverCount": 1 } }, "displayName": "Physical Contact" } ]
  },
  { "ID": "com_touch_hand", "displayName": "Touch Hand",
    "parentCOMID": "com_touch_group",
    "AcceptanceCheck": { "parentMod": 0 },
    "variants": [ { "displayName": "Touch Hand" } ],
    "description_begin": { ... }
  },
  { "ID": "com_touch_hair", "displayName": "Touch Hair",
    "parentCOMID": "com_touch_group",
    "AcceptanceCheck": { "parentMod": -1 },
    "variants": [ { "displayName": "Touch Hair" } ],
    "description_begin": { ... }
  }
]
```

---

## Existing COMs That Need Migration

Some existing COMs that currently live as standalone buttons could be grouped:
- Touch-type COMs (`com_touch_head`, etc.) could share a "Physical Contact" group parent
- Food/meal COMs (from `COM_job.json`) could share an "Eat" parent
- These can be migrated later — the system is backwards compatible:
  `parentCOMID = ""` means "behave exactly as before"

---

## Files to Touch

| File | Change |
|---|---|
| `Assets/Scripts/JobCOM/COM.cs` | Add `childCOMIDs`, `parentCOMID`, `parentMod` on Acceptance/Difficulty; add `IsGroupParent`/`IsGroupChild`; add `ParentCOM`; add `GetEffectiveBaseAcceptance/DC()` helpers |
| `Assets/Scenes/GameLoop/MainMenu/scr_panel_COMmanager.cs` | Filter children from main list; add `MakeCOMGroupButton()`; add child submenu logic |
| `Assets/Scripts/LLM/LLMUtils.cs` | Skip child COMs in iteration; print grouped output for parent COMs |
| Wherever `com.baseAcceptanceValue` / `com.baseD20Check` is used at runtime | Replace with `com.GetEffectiveBaseAcceptance(...)` / `com.GetEffectiveBaseDC(...)` calls for child correctness |
| New prefab: `prefab_COMGroupButton` | Visual for the expandable group button |
| New prefab/panel: child submenu panel | Visual list of children inside the group |
| JSON COM definition files | Add `childCOMIDs`/`parentCOMID` to relevant COMs |
| Localization JSON | Add display names and tooltip strings for group parents |

---

## Implementation Order

1. **Data**: Add `childCOMIDs`, `parentCOMID`, `parentMod` fields to `COM` + nested classes.
   → JSON can be authored. No behavior change yet (new fields ignored).

2. **Acceptance/DC helpers**: Implement `GetEffectiveBaseAcceptance/DC()`.
   Replace call sites. Verify existing non-grouped COMs unchanged.

3. **UI — filter children**: In `ValidateAll`, skip child COMs from main button list.
   → Grouped children vanish from main UI (group button doesn't exist yet).

4. **UI — group button**: Implement `MakeCOMGroupButton()` + child submenu panel.
   → Group parent shows as button; click opens child list.

5. **LLM output**: Update `CollectCOMInfo()` to output grouped format.

6. **Author first group**: Create the "Physical Contact" or "Watch TV" group in JSON, test end-to-end.

---

## Edge Cases

- **Group parent with all children invalid**: Gray the parent button. Optionally hide it if `HideWhenInvalid`.
- **Child with no extra requirements**: Always valid when parent is valid; no additional check needed.
- **Child that is also used standalone elsewhere**: Currently unsupported — a COM either has `parentCOMID` or it doesn't. If standalone use is needed, make a separate standalone COM entry.
- **Nested groups** (parent of parent): Not planned. `IsGroupParent` and `IsGroupChild` are mutually exclusive by convention. Don't author them nested.
- **LLM output size**: With grouped format, LLM output is more concise. Children print minimal info (just delta + unique requirement), not full duplicate validation context.
- **Serialization**: `childCOMIDs = null` (not `new List<string>()`) is the correct default so JSON omits the field when absent. Same for `parentCOMID = ""` (already the default string behavior).
