using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Controller for the supplied wardrobe canvas prefab. Load through LoadCanvasIntoScene, then InitializeWithArgument.
/// Layout, scrolling and controls are supplied by prefabs.
/// Fixed scr_SelectableText option IDs: 1 save, 2 save and wear, 3 random, 4 copy chooser, 5 clear pieces,
/// 6 remove assignment, 10/11/12 replace skin/inner/outer, 13 limit to venue, 14 back to inventory, 9999 cancel.
/// Row prefab buttons use optionID -1; the menu assigns their IDs after instantiation.
/// </summary>
public class scr_WardrobeEditor : scr_Menu, IPointerClickHandler
{
    public const int SaveID = 1, SaveAndWearID = 2, RandomID = 3, CopyID = 4, ClearID = 5, RemoveID = 6;
    public const int SkinID = 10, InnerID = 11, OuterID = 12, LocaleID = 13, InventoryID = 14, CancelID = 9999;

    [Header("Canvas references")]
    public scr_HoverableText title, inventorySource, status;
    public TMP_InputField nameInput;
    public GameObject inventoryPanel, copyPanel;
    public RectTransform inventoryList, outfitList, variantList, copyList;

    [Header("Row prefabs (may use the same prefab)")]
    public scr_WardrobeEditEntry prefab_piece;
    public scr_WardrobeEditEntry prefab_option;

    Character_Trainable character;
    OutfitSet draft;
    List<OutfitPiece> available;
    readonly List<scr_WardrobeEditEntry> entries = new List<scr_WardrobeEditEntry>();
    int variantIndex = -1;
    bool showCopies, draftValid;
    string notice = "", validationReason = "";
    List<OutfitPiece> Pieces => variantIndex < 0 ? draft.pieces : draft.variants[variantIndex].pieces;
    bool Editable => draft != null && character?.Wardrobe?.CanEdit(draft.key) == true;

    /// <summary>The opening button checks these Inspector references before loading the canvas.</summary>
    public bool IsConfigured(out string reason)
    {
        bool valid = m_Canvas != null && title != null && inventorySource != null && status != null && nameInput != null
            && inventoryPanel != null && copyPanel != null && inventoryList != null && outfitList != null
            && variantList != null && copyList != null && prefab_piece != null && prefab_piece.IsConfigured
            && prefab_option != null && prefab_option.IsConfigured;
        reason = valid ? "" : "Assign the wardrobe editor's canvas, text fields, panels, list containers and row prefabs.";
        return valid;
    }

    protected override void Awake()
    {
        base.Awake();
        if (m_Canvas != null) m_Canvas.overrideSorting = true;
    }

    public override void Initialize()
    {
        if (initialized) return;
        base.Initialize();
        // OnEnable may run before the caller supplies arguments. Bind only prefab controls here.
        foreach (var button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            if (button.optionID == -1) continue;
            if (buttonsByID.ContainsKey(button.optionID))
            {
                Debug.LogError("Wardrobe canvas has duplicate optionID " + button.optionID, this);
                continue;
            }
            button.Initialize(this, new Button_MenuAction(this, button.optionID));
            if (button.optionID >= SkinID && button.optionID <= LocaleID) button.isButtonToggle = true;
            buttonsByID.Add(button.optionID, button);
            validatorsByID.Add(button.optionID, button.Validator);
        }
        if (nameInput != null) nameInput.onValueChanged.AddListener(OnNameChanged);
    }

    public void InitializeWithArgument(Character_Trainable c, string key, scr_Menu.Action onExit)
    {
        onSelfExit = onExit;
        if (!initialized) Initialize();
        if (!IsConfigured(out var reason))
        {
            Debug.LogError(reason, this);
            Close();
            return;
        }
        character = c;
        if (character?.Wardrobe == null) { Close(); return; }
        draft = character.Wardrobe.BeginEdit(key, out int removed);
        if (draft == null) { Close(); return; }
        available = character.Wardrobe.AvailablePieces(key);
        variantIndex = -1;
        showCopies = false;
        notice = removed > 0 ? removed + " unavailable piece(s) removed from this draft. Cancel keeps the saved outfit." : "";
        nameInput.SetTextWithoutNotify(draft.name);
        title.SetText("Wardrobe — " + OutfitUtility.KeyDisplayName(key));
        inventorySource.SetText("Clothing from " + character.Wardrobe.EditingFaction(key).FactionDisplayName
            + ". Saving does not consume items; saved clothes remain usable after the originals are sold.");
        Rebuild();
    }

    void OnNameChanged(string value)
    {
        if (!Editable) return;
        draft.name = value;
        ValidateAll();
    }

    public override void ValidateAll()
    {
        if (draft != null && status != null)
        {
            draftValid = character.Wardrobe.ValidateDraft(draft, out validationReason);
            status.SetText((draftValid ? "Valid outfit." : "Incomplete outfit: " + validationReason) + "\n" + notice
                + (Editable ? "" : "\nThe responsible faction is no longer player managed.")
                + (draftValid ? "" : "\nYou may save an incomplete outfit and finish it later."));
            if (nameInput != null) nameInput.interactable = Editable;
            ToggleControl(SkinID, draft.overrideSkin);
            ToggleControl(InnerID, draft.overrideInner);
            ToggleControl(OuterID, draft.overrideOuter);
            ToggleControl(LocaleID, draft.limitToLocaleFaction);
        }
        base.ValidateAll();
    }

    void ToggleControl(int id, bool selected)
    {
        if (buttonsByID.TryGetValue(id, out var button)) button.Toggle(true, selected);
    }

    void Rebuild()
    {
        ClearEntries();
        inventoryPanel.SetActive(!showCopies);
        copyPanel.SetActive(showCopies);
        MakeEntry(prefab_option, variantList, "Base outfit", "", "ui_wardrobe_edit", () => { variantIndex = -1; Rebuild(); }, selected: variantIndex < 0);
        for (int i = 0; i < draft.variants.Count; i++)
        {
            int index = i;
            var variant = draft.variants[i];
            string details = string.Join(", ", variant.requireTags_all.Concat(variant.requireTags_any));
            MakeEntry(prefab_option, variantList, "Variant " + (i + 1) + " " + variant.group, details, "ui_wardrobe_edit",
                () => { variantIndex = index; Rebuild(); }, selected: variantIndex == index);
        }
        if (showCopies) PopulateCopies();
        else foreach (var piece in available)
        {
            var candidate = piece;
            var probe = new Item_Instance(candidate.baseID, candidate.nameOverwrite);
            string restrictReason = null;
            bool canWear = probe.Comp_Equippable == null || probe.Comp_Equippable.CanBeWornBy(character, out restrictReason);
            string disableReason = canWear ? null : restrictReason;
            MakeEntry(prefab_piece, inventoryList, piece.DisplayName, PieceDetails(piece), "ui_wardrobe_addPiece", () =>
            {
                Pieces.Add(candidate.Clone());
                Rebuild();
            }, () => canWear && !Pieces.Any(p => p.baseID == candidate.baseID), selected: Pieces.Any(p => p.baseID == piece.baseID), disabledTooltip: disableReason);
        }
        for (int i = 0; i < Pieces.Count; i++)
        {
            int index = i;
            var piece = Pieces[i];
            MakeEntry(prefab_piece, outfitList, piece.DisplayName, PieceDetails(piece), "ui_wardrobe_removePiece", () => { Pieces.RemoveAt(index); Rebuild(); });
        }
        ValidateAll();
    }

    static string PieceDetails(OutfitPiece piece)
    {
        var comp = new Item_Instance(piece.baseID, piece.nameOverwrite).Comp_Equippable;
        return comp == null ? "" : comp.equipLayer + ": " + string.Join(", ", comp.equipSlot);
    }

    void PopulateCopies()
    {
        var sources = new List<Character_Trainable> { character };
        foreach (var other in character.Wardrobe.EditingFaction(draft.key).ManagedChara)
            if (other?.Wardrobe != null && !sources.Contains(other)) sources.Add(other);
        foreach (var sourceCharacter in sources)
        foreach (var outfit in sourceCharacter.Wardrobe.Outfits.Concat(sourceCharacter.Wardrobe.PlayerOutfits))
        {
            var source = outfit;
            MakeEntry(prefab_option, copyList, sourceCharacter.FirstName + ": " + source.DisplayName,
                "Unavailable pieces will be removed from the copy.", "ui_wardrobe_copy", () =>
                {
                    string key = draft.key;
                    draft = source.Clone(); draft.key = key; draft.playerEdited = true;
                    var stock = new HashSet<string>(available.Select(p => p.baseID));
                    draft.pieces.RemoveAll(p => p == null || !stock.Contains(p.baseID));
                    foreach (var variant in draft.variants) variant.pieces.RemoveAll(p => p == null || !stock.Contains(p.baseID));
                    nameInput.SetTextWithoutNotify(draft.name);
                    variantIndex = -1; showCopies = false;
                    Rebuild();
                });
        }
    }

    void MakeEntry(scr_WardrobeEditEntry prefab, RectTransform container, string label, string details, string actionLabel,
        System.Action action, Func<bool> enabled = null, bool selected = false, string disabledTooltip = null)
    {
        var row = Instantiate(prefab);
        row.transform.SetParent(container, false);
        int id = AssertUniqueHash(row.GetHashCode());
        row.button.Initialize(this, new Button_Entry(this, action, enabled, disabledTooltip));
        row.button.optionID = id;
        row.button.isButtonToggle = true;
        row.button.Toggle(true, selected);
        row.InitializeData(label, details, actionLabel);
        buttonsByID.Add(id, row.button);
        validatorsByID.Add(id, row.button.Validator);
        entries.Add(row);
    }

    void ClearEntries()
    {
        foreach (var row in entries)
        {
            if (row == null) continue;
            int id = row.button.optionID;
            buttonsByID.Remove(id);
            if (validatorsByID.TryGetValue(id, out var validator)) validator.Destroy();
            validatorsByID.Remove(id);
            row.gameObject.SetActive(false);
            Destroy(row.gameObject);
        }
        entries.Clear();
    }

    public override void Notify(int optionID)
    {
        if (!validatorsByID.TryGetValue(optionID, out var validator)) return;
        if (!validator.IsButtonValid()) { ValidateAll(); return; }
        if (validator is I_ButtonClickable clickable) clickable.OnClickButton();
        if (this != null) ValidateAll();
    }

    void HandleAction(int id)
    {
        switch (id)
        {
            case CancelID: Close(); return;
            case SaveID: if (Commit()) Close(); return;
            case SaveAndWearID:
                if (!character.Wardrobe.ValidateDraft(draft, out notice)) break;
                if (Commit()) { character.Wardrobe.ApplyOutfit(character.Wardrobe.GetPlayerOutfit(draft.key)); Close(); }
                return;
            case RandomID: Randomize(); return;
            case CopyID: showCopies = true; Rebuild(); return;
            case InventoryID: showCopies = false; Rebuild(); return;
            case ClearID: Pieces.Clear(); Rebuild(); return;
            case RemoveID: character.Wardrobe.RemovePlayerOutfit(draft.key); Close(); return;
            case SkinID: draft.overrideSkin = !draft.overrideSkin; break;
            case InnerID: draft.overrideInner = !draft.overrideInner; break;
            case OuterID: draft.overrideOuter = !draft.overrideOuter; break;
            case LocaleID: draft.limitToLocaleFaction = !draft.limitToLocaleFaction; break;
        }
    }

    bool Commit() => character.Wardrobe.SavePlayerOutfit(draft, out notice);

    void Randomize()
    {
        var original = draft.Clone();
        for (int attempt = 0; attempt < 40; attempt++)
        {
            draft.pieces.Clear(); draft.variants.Clear(); variantIndex = -1;
            draft.overrideSkin = draft.overrideInner = draft.overrideOuter = true;
            var used = new HashSet<(BodyEquipLayer, BodyPartEquipSlot)>();
            foreach (var piece in available.OrderBy(p => UnityEngine.Random.value))
            {
                var comp = new Item_Instance(piece.baseID, piece.nameOverwrite).Comp_Equippable;
                if (comp == null || !comp.CanBeWornBy(character, out _, explain: false)
                    || comp.equipSlot.Any(s => used.Contains((comp.equipLayer, s))) || UnityEngine.Random.value < .3f) continue;
                draft.pieces.Add(piece.Clone());
                foreach (var slot in comp.equipSlot) used.Add((comp.equipLayer, slot));
            }
            if (!character.Wardrobe.ValidateDraft(draft, out _)) continue;
            notice = "Random outfit created from available clothing.";
            Rebuild(); return;
        }
        draft = original;
        notice = "Could not find a valid random combination. Select clothing manually.";
        Rebuild();
    }

    void Close() => scr_System_SceneManager.current.UnloadLastCanvasFromScene(typeof(scr_WardrobeEditor));

    public void OnPointerClick(PointerEventData eventData)
    {
        if ((eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData))
            || (eventData.rawPointerPress != null && eventData.rawPointerPress.GetComponent<scr_Menu_CharaDetail>() != null)) Close();
    }

    protected override void OnDestroy()
    {
        if (nameInput != null) nameInput.onValueChanged.RemoveListener(OnNameChanged);
        base.OnDestroy(); // The scene manager owns normal-close callbacks; destruction/shutdown never commits a draft.
    }

    class Button_MenuAction : ButtonValidator, I_ButtonClickable
    {
        readonly scr_WardrobeEditor menu;
        readonly int id;
        public Button_MenuAction(scr_WardrobeEditor menu, int id) : base(menu) { this.menu = menu; this.id = id; }
        public override bool IsButtonValid()
        {
            state = ButtonValidator_States.Valid; tooltip = "";
            if (id == CancelID) return true;
            if (!menu.Editable) { state = ButtonValidator_States.Invalid; tooltip = "The responsible faction must be player managed."; return false; }
            if (id == SaveAndWearID && !menu.draftValid) { state = ButtonValidator_States.Invalid; tooltip = menu.validationReason; return false; }
            if (id == SaveID && !menu.draftValid) { state = ButtonValidator_States.Conflict; tooltip = menu.validationReason + " You may save this draft and finish it later."; }
            if (id == InventoryID) return menu.showCopies;
            if (id == CopyID) return !menu.showCopies;
            return id >= SaveID && id <= RemoveID || id >= SkinID && id <= LocaleID;
        }
        public void OnClickButton() => menu.HandleAction(id);
    }

    class Button_Entry : ButtonValidator, I_ButtonClickable
    {
        readonly scr_WardrobeEditor menu;
        readonly System.Action action;
        readonly Func<bool> enabled;
        readonly string disabledTooltip;
        public Button_Entry(scr_WardrobeEditor menu, System.Action action, Func<bool> enabled, string disabledTooltip = null) : base(menu)
        { this.menu = menu; this.action = action; this.enabled = enabled; this.disabledTooltip = disabledTooltip; }
        public override bool IsButtonValid()
        {
            bool valid = menu.Editable && (enabled == null || enabled());
            state = valid ? ButtonValidator_States.Valid : ButtonValidator_States.Invalid;
            tooltip = valid ? "" : !menu.Editable ? "The responsible faction must be player managed."
                : !string.IsNullOrEmpty(disabledTooltip) ? disabledTooltip : "This piece is already included.";
            return valid;
        }
        public void OnClickButton() => action();
    }
}
