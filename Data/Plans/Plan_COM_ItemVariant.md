# Plan: COM Target Item Variants

## Goal
Extend `COM_Variant` to support **item-targeting variants** — variants that are only valid when
the faction's inventory contains a specific item or keyword. These variants can carry additional
DC modifiers, requirements, and results, and KOJO descriptions can branch per chosen variant.

Example: A "Play Video Games" COM gains item variants "Play Nintendo Kart" (requires that item
in faction inventory) and "Play Bloodborne" (requires item tagged `hard_game`).

---

## Current System Summary

| Concept | Location | Key Detail |
|---|---|---|
| `COM` | `COM.cs` | Has `List<COM_Variant>`, base `requirements`, `results`, `DifficultyCheck`, `description_*` |
| `COM_Variant` | `COM.cs` inner class | Has `requirements`, `description_*`, `displayName`, `useBaseDescription`, `useAnothersDescription` |
| Variant selection | `COM.GetValidVariant()` | Returns **highest-index** valid variant; validates base req first, then per-variant req |
| `validVariant` | `ActionPackage` | Stores chosen variant index; used for all description lookups |
| `COM_Descriptions` | `COM_Descriptions.cs` | Condition-gated text entries; conditions = RandChance, Job, Chara, Package validators |
| `RequireFactionExisting` | `COM_Requirements.cs` | Already validates faction inventory for `inventoryItemBaseID` |

### Variant selection flow
```
ActionPackage.Validate()
  → ActionPackage.Evaluate()
      → COM.GetValidVariant(tooltip, doers, receivers, ...)
            iterates variants, returns highest-index valid one
      → stores in ActionPackage.validVariant
      → creates EvaluationPackages (one per actor-pair)

ActionPackage.LogMessage_Begin/Ongoing/After()
  → targetCOM.variants[COMVariantID].GetDescription_*(ownerCOM, ap)
      → description_*.GetText(ap)
          → Description_Entry.GetValidText(ap)
              → evaluates COMDesc_Conditions per EvaluationPackage
```

---

## Design: What to Add

### 1. New class `COM_ItemTarget` (inner class of `COM_Variant`)

```csharp
[System.Serializable]
public class COM_ItemTarget
{
    public string itemBaseID = "";    // match by specific item base ID
    public string itemKeyword = "";   // match by item tag/keyword
    public int minimumCount = 1;

    // Optional DC modifier when this item variant is chosen
    public int difficultyModifier = 0;

    public bool isValid => itemBaseID != "" || itemKeyword != "";

    /// <summary>
    /// Returns true if faction inventory satisfies the item requirement.
    /// </summary>
    public bool ValidateFaction(I_IsJobGiver faction, out string tooltip)
    {
        tooltip = "";
        if (!isValid) return true;
        if (faction == null || faction.Inventory == null)
        {
            tooltip = "no faction inventory";
            return false;
        }
        if (itemBaseID != "" && faction.Inventory.GetItemCount(itemBaseID) < minimumCount)
        {
            var item = scr_System_Serializer.current.index_Item_Base.GetByID(itemBaseID);
            tooltip = LocalizeDictionary.QueryThenParse("ui_RequireFactionExisting_inventoryItemBaseID")
                .Replace("$faction$", (faction as Manageable)?.FactionDisplayName ?? "-")
                .Replace("$name$", item?.DisplayName ?? itemBaseID);
            return false;
        }
        if (itemKeyword != "" && faction.Inventory.GetItemCount_byTag(itemKeyword) < minimumCount)
        {
            tooltip = LocalizeDictionary.QueryThenParse("ui_RequireFactionItem_keyword")
                .Replace("$faction$", (faction as Manageable)?.FactionDisplayName ?? "-")
                .Replace("$keyword$", itemKeyword);
            return false;
        }
        return true;
    }
}
```

### 2. Fields to add to `COM_Variant`

```csharp
// A stable ID string for this variant, used by description conditions
public string variantID = "";

// If non-null, this variant is an item variant and is only chosen when item is available
public COM_ItemTarget itemTarget = null;

// Optional extra results applied in addition to base COM results when this variant is chosen
public COM_Results additionalResults = null;
```

**`COM_Variant` does NOT get a `Difficulty` override field** — the DC modifier lives inside
`COM_ItemTarget.difficultyModifier` to keep it simple. If richer per-variant difficulty control
is needed later, a full `Difficulty` override can be added then.

---

## Variant Selection Logic Change

### Problem
`GetValidVariant()` currently returns the **highest-index** valid variant.
Item variants need to be resolved differently: pick **one at random** from all valid item variants.
Regular variants remain the existing "highest-index wins" logic, used as fallback.

### New logic in `COM.GetValidVariant()`

```
1. Run all existing pre-checks (base requirements, sex/faction checks) — unchanged.
2. Separate variants into two lists:
     - itemVariants:  variants where itemTarget != null
     - regularVariants: variants where itemTarget == null
3. From itemVariants, collect all that pass BOTH:
     - their regular requirements (ValidateCondition)
     - their itemTarget.ValidateFaction(faction)
4. If validItemVariants.Count > 0:
     - Pick one at RANDOM (Utility.GetRandIndexFromListCount equivalent)
     - Return its index in the full variants list
5. Else: fall back to existing "highest-index wins" logic among regularVariants.
```

### Faction parameter
`GetValidVariant()` must receive `I_IsJobGiver faction` (nullable).
When null, all `itemTarget.ValidateFaction` checks are skipped (item variants excluded),
preserving backwards compatibility for callers that don't have faction context.

**Callers to update**: Find all `GetValidVariant(...)` call sites in ActionPackage or elsewhere
and pass the faction from `job.FactionOwner` (or whatever context is available).

---

## Description System Extension

### New condition validator: `Validator_TargetItem`

Add to `COM_Descriptions.Description_Entry.COMDesc_Conditions`:

```csharp
public class Validator_TargetItem
{
    // If empty, condition passes for ANY item variant (as long as one was chosen)
    public string variantID = "";

    public bool Validate(ActionPackage ap)
    {
        if (ap == null || ap.COMVariantID < 0) return false;
        var variant = ap.targetCOM.variants[ap.COMVariantID];
        if (variant.itemTarget == null) return false;           // not an item variant
        if (variantID != "" && variant.variantID != variantID) return false;
        return true;
    }
}
```

Add to the `Validate(ref EvaluationPackage evp)` dispatcher on `COMDesc_Conditions`:
```csharp
public Validator_TargetItem validateTargetItem = null;
// In Validate(): if (validateTargetItem != null) returnVal = returnVal && validateTargetItem.Validate(evp.Package);
```

Also add the `ActionPackage ap` overload path to `Validate(ActionPackage ap)` used in the
`GetValidText(List<string> list, ActionPackage ap)` branch.

---

## Applying Additional Results

In `ActionPackage.ApplyResults()` (or wherever `COM_Results.ApplyResults()` is called per character),
after applying the base COM results, check and apply variant additional results:

```csharp
// Existing:
p.targetCOM.results.ApplyResults(job, p, evp, c, log);

// New:
if (p.COMVariantID >= 0)
{
    var variant = p.targetCOM.variants[p.COMVariantID];
    if (variant.additionalResults != null)
        variant.additionalResults.ApplyResults(job, p, evp, c, log);
}
```

---

## Applying Difficulty Modifier

Find where `baseD20Check` is computed / rolled. Add to the final DC calculation:

```csharp
if (ap.COMVariantID >= 0)
{
    var variant = ap.targetCOM.variants[ap.COMVariantID];
    if (variant.itemTarget != null)
        dcTotal += variant.itemTarget.difficultyModifier;
}
```

This should be done wherever `EvaluationPackage.Evaluate()` computes the difficulty roll.

---

## JSON Schema

### Item variants alongside regular variants
```json
{
  "ID": "com_daily_play_videogames",
  "displayName": "com_daily_play_videogames_displayName",
  "variants": [

    // --- Regular variants (existing logic) ---
    {
      "requirements": { "requirement": { "doerCount": 1 } },
      "displayName": "com_daily_play_videogames_displayName"
    },

    // --- Item variants (new) ---
    {
      "variantID": "nintendo_kart",
      "displayName": "com_daily_play_nintendo_kart_displayName",
      "requirements": { "requirement": { "doerCount": 1 } },
      "itemTarget": {
        "itemBaseID": "item_game_nintendo_kart",
        "minimumCount": 1,
        "difficultyModifier": 3
      },
      "additionalResults": {
        "results_character": [
          { "entry_conditions": { "applyToDoer": true }, "entry_results": { "type": "statMod_EN", "value": "1" } }
        ]
      },
      "description_begin": {
        "Entries": [
          { "keepLooking": false, "texts": ["com_daily_play_nintendokart_begin_1"] }
        ]
      },
      "useBaseDescription": false
    },

    {
      "variantID": "bloodborne",
      "displayName": "com_daily_play_bloodborne_displayName",
      "requirements": { "requirement": { "doerCount": 1 } },
      "itemTarget": {
        "itemKeyword": "hard_game",
        "difficultyModifier": -5
      },
      "description_begin": {
        "Entries": [
          { "keepLooking": false, "texts": ["com_daily_play_bloodborne_begin_1"] }
        ]
      },
      "useBaseDescription": false
    }
  ]
}
```

### Alternative: condition-gated text in base description (Validator_TargetItem)
Item variants can also override text via conditions in the COM's own `description_begin`
instead of (or in addition to) per-variant `description_begin`:

```json
"description_begin": {
  "Entries": [
    {
      "conditions": [{ "validateTargetItem": { "variantID": "nintendo_kart" } }],
      "keepLooking": false,
      "texts": ["com_daily_play_nintendokart_begin_1"]
    },
    {
      "conditions": [{ "validateTargetItem": { "variantID": "bloodborne" } }],
      "keepLooking": false,
      "texts": ["com_daily_play_bloodborne_begin_1"]
    },
    {
      "keepLooking": false,
      "texts": ["com_daily_play_videogames_begin_1"]
    }
  ]
}
```

Both approaches work. The **per-variant `description_begin`** is cleaner for long per-variant
texts. The **condition-gated base description** is better when item variants share most text
with the base and only add a short suffix.

---

## Files to Touch

| File | Change |
|---|---|
| `Assets/Scripts/JobCOM/COM.cs` | Add `variantID`, `itemTarget`, `additionalResults` to `COM_Variant`; add `COM_ItemTarget` inner class; modify `GetValidVariant()` selection logic; add `faction` param |
| `Assets/Scripts/JobCOM/COM_Descriptions.cs` | Add `Validator_TargetItem` to `COMDesc_Conditions`; wire into both `Validate(ref EvaluationPackage)` and `Validate(ActionPackage)` paths |
| `Assets/Scripts/JobCOM/COM_Requirements.cs` | Move `COM_ItemTarget` here if preferred for organization; optionally add `Inventory.GetItemCount_byTag()` if missing |
| ActionPackage file (wherever results are applied) | Apply `variant.additionalResults` after base results |
| ActionPackage / EvaluationPackage (DC roll) | Add `itemTarget.difficultyModifier` to difficulty roll |
| All `GetValidVariant(...)` call sites | Pass `faction` (I_IsJobGiver) as new parameter |
| Localization JSON(s) | Add `ui_RequireFactionItem_keyword` string; add any item-variant text strings |

---

## Implementation Order (recommended)

1. **Add `COM_ItemTarget` class** and new fields to `COM_Variant` (data layer only, no logic changes).
   → At this point, JSON can already define item variants; they'll just be ignored.

2. **Extend `GetValidVariant()`** with item-variant selection logic + faction param.
   Wire up call sites to pass faction.
   → Item variants now get selected (or skipped if no faction context).

3. **Apply `additionalResults`** in the result-application path.
   → Item variant results now fire.

4. **Apply `difficultyModifier`** in DC computation.
   → Difficulty adjusts per chosen item.

5. **Add `Validator_TargetItem`** to `COM_Descriptions`.
   → KOJO descriptions can now branch on chosen item variant.

6. **Author JSON content** for first item-variant COM, add localization strings, test end-to-end.

---

## Edge Cases to Consider

- **Multiple valid item variants**: handled by random selection (step 4 of selection logic).
- **Item variant + regular variant conflict**: item variants take priority; regular variants are fallback only.
- **No faction context**: when `GetValidVariant` is called without faction (e.g. during UI validation), `itemTarget` variants are simply excluded. The UI will show the regular variant's `displayName`. A separate UI pass (with faction) could show "Item variant available" hints.
- **`useBaseDescription` default**: item variants should default `useBaseDescription = false` unless the author wants the generic text appended. The JSON default remains `true` for backwards compat, so authors must explicitly set it.
- **`additionalResults` vs base `results`**: both apply. The variant's additional results are supplemental, not replacements. If replacement behavior is needed, add a `replaceBaseResults` bool later.
- **Serialization**: `COM_ItemTarget` uses `[System.Serializable]` + Newtonsoft.Json. Since it's a reference type on `COM_Variant`, it deserializes as null when absent — correct behavior for "not an item variant".
