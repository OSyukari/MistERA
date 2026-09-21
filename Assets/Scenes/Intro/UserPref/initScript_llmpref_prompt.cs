using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class initScript_llmpref_prompt : MonoBehaviour
{
    public RectTransform selfRect;

    bool initialized = false;

    public scr_SelectableText prefab_presetbutton;
    public RectTransform rect_playerPresets;

    public RectTransform rect_messageRoot;
    //public scr_SelectableText btn_addRootMessage;

    scr_MenuCanvas_UserPrefs parent = null;
    public void LoadPanel(scr_MenuCanvas_UserPrefs parent)
    {
        if (initialized) return;
        initialized = true;

        this.parent = parent;

        currentPresetName.self_inputfield.onValueChanged.AddListener(s => { draftName = s; parent.ValidateAll(); });

        RebuildPresetList();
        LoadPreset(scr_System_CentralControl.current.LLMSetting.currentPromptTemplateId);
    }

    public scr_inputFieldLink currentPresetName;// show the current preset name.

    //public scr_SelectableText btn_deletePreset; // delete current preset, only valid if current preset is player preset
    //public scr_SelectableText btn_createPreset; // create a new preset (copy of default) as an in-memory draft; nothing is written to disk until Save
    //public scr_SelectableText btn_savePreset;   // save/overwrite the current draft to the preset name file

    // function that loads current preset
    // every button need to be tracked, so that:
    // 1 - when deleting a node (and recursively deleting all its childs) the buttonvalidators are correctly unregistered
    // 2 - when loading a new preset, the old preset button and UI rect need to all be cleaned up properly

    string loadedPresetId = null;         // null => viewing the default/fallback (always read-only), unless editingNewUnsavedPreset
    bool editingNewUnsavedPreset = false; // true right after "Create" and until the first Save persists it under a real id
    LLMPresetTemplateData draft = null;   // in-memory copy of whichever preset is being viewed/edited
    string draftName = "";

    List<presetEditor_llm_Message> rootNodes = new List<presetEditor_llm_Message>();
    List<int> presetRowButtonIDs = new List<int>();

    Coroutine loadCoroutine = null;

    public bool AllowEdit { get { return loadedPresetId != null || editingNewUnsavedPreset; } }

    void OnDisable()
    {
        if (loadCoroutine == null) return;
        StopCoroutine(loadCoroutine);
        loadCoroutine = null;
        scr_System_LoadingScreen.current?.Hide();
    }

    void RebuildPresetList()
    {
        Utility.DestroyAllChildrenFrom(rect_playerPresets);
        foreach (var id in presetRowButtonIDs) parent.UnregisterButton(id);
        presetRowButtonIDs.Clear();

        // default preset has its own dedicated static button (see scr_MenuCanvas_UserPrefs case 1804) - not listed here
        foreach (var id in scr_System_CentralControl.current.PlayerPromptPresetIDs) AddPresetRow(id);
    }

    void AddPresetRow(string id)
    {
        var button = Instantiate(prefab_presetbutton);
        button.transform.SetParent(rect_playerPresets, false);

        int optionID = parent.AssertUniqueHashPublic(button.GetHashCode());
        var validator = new ButtonValidator_SelectPromptPreset(parent, this, id, button);
        button.optionID = optionID;
        button.Initialize(parent, validator);
        parent.RegisterButton(optionID, button, validator);
        button.SetText(id);

        presetRowButtonIDs.Add(optionID);
    }

    string pendingLoad = null;
    bool hasPendingLoad = false;

    void LoadPresetBegin()
    {
        if (hasPendingLoad)
        {
            if (loadCoroutine != null) { StopCoroutine(loadCoroutine); scr_System_LoadingScreen.current?.Hide(); }
            loadCoroutine = StartCoroutine(LoadPresetCoroutine(pendingLoad));
            pendingLoad = null;
            hasPendingLoad = false;
        }
    }

    public void LoadPreset(string id)
    {
        pendingLoad = id;
        hasPendingLoad = true;
        parent.validateAll_postLoadInit = LoadPresetBegin;
    }

    IEnumerator LoadPresetCoroutine(string id)
    {
        foreach (var node in rootNodes) node.DestroyAndUnregister();
        rootNodes.Clear();

        editingNewUnsavedPreset = false;
        loadedPresetId = (string.IsNullOrEmpty(id) || id == scr_System_CentralControl.DefaultPresetID) ? null : id;

        var source = loadedPresetId == null ? scr_System_CentralControl.current.LLMPresetTemplate : scr_System_CentralControl.current.GetPreset(loadedPresetId);
        draft = scr_System_CentralControl.current.ClonePreset(source);

        draftName = loadedPresetId ?? scr_System_CentralControl.DefaultPresetID;
        currentPresetName.self_inputfield.SetTextWithoutNotify(draftName);
        currentPresetName.self_inputfield.interactable = AllowEdit;

        yield return BuildRootRowsCoroutine();

        parent.ValidateAll();
    }

    /// <summary>
    /// "Create" button: clones the shipped default into a fresh in-memory draft and shows it as
    /// fully editable, without touching disk - the player names it (via currentPresetName) and
    /// commits it with Save. Distinct from LoadPreset(id), which always reflects real disk state.
    /// </summary>
    void LoadNewUnsavedPreset()
    {
        if (loadCoroutine != null) { StopCoroutine(loadCoroutine); scr_System_LoadingScreen.current?.Hide(); }
        loadCoroutine = StartCoroutine(LoadNewUnsavedPresetCoroutine());
    }

    IEnumerator LoadNewUnsavedPresetCoroutine()
    {
        foreach (var node in rootNodes) node.DestroyAndUnregister();
        rootNodes.Clear();

        loadedPresetId = null;
        editingNewUnsavedPreset = true;

        draft = scr_System_CentralControl.current.ClonePreset(scr_System_CentralControl.current.LLMPresetTemplate);

        draftName = "";
        currentPresetName.self_inputfield.SetTextWithoutNotify(draftName);
        currentPresetName.self_inputfield.interactable = AllowEdit;

        yield return BuildRootRowsCoroutine();
    }

    /// <summary>
    /// Recursively instantiating a whole tree in one frame is what causes the reported hitch -
    /// yielding once per root-level message spreads that cost across frames instead (a single
    /// root's own nested subtree still builds synchronously in one frame, but the preset as a
    /// whole no longer has to). Drives scr_System_LoadingScreen for visible progress meanwhile,
    /// and re-validates every button once done since ValidateAll() already ran (via Notify()) for
    /// the click that triggered this load, before any of these rows existed.
    /// </summary>
    IEnumerator BuildRootRowsCoroutine()
    {
        int total = draft.messages.Count;

        if (total > 0) scr_System_LoadingScreen.current?.Show(LocalizeDictionary.QueryThenParse("ui_prefs_llm_preset_loading", "Loading preset..."));

        for (int i = 0; i < total; i++)
        {
            AddRootMessageRow(draft.messages[i]);
            scr_System_LoadingScreen.current?.SetProgress((float)(i + 1) / total, $"{i + 1}/{total}");
            yield return null;
        }

        if (total > 0) scr_System_LoadingScreen.current?.Hide();
        parent.ValidateAll();
        loadCoroutine = null;
    }

    void AddRootMessageRow(LLMMessage node)
    {
        var row = Instantiate(prefab_presetNode);
        row.transform.SetParent(rect_messageRoot, false);
        row.Load(parent, this, node, draft.messages, AllowEdit);
        rootNodes.Add(row);
    }

    // recursively build UI
    public presetEditor_llm_Message prefab_presetNode;
    // use the presetEditor message script's btn_nodeType validator to distinguish different types of nodes.


    public class ButtonValidator_SelectPromptPreset : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        initScript_llmpref_prompt panel;
        string id;
        scr_SelectableText button;
        public ButtonValidator_SelectPromptPreset(scr_MenuCanvas_UserPrefs parent, initScript_llmpref_prompt panel, string id, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.panel = panel;
            this.id = id;
            this.button = button;
        }

        public override bool IsButtonValid()
        {
            bool isCurrent = !panel.editingNewUnsavedPreset && (panel.loadedPresetId ?? scr_System_CentralControl.DefaultPresetID) == id;
            button.Toggle(true, isCurrent);
            return true;
        }

        public void OnClickButton()
        {
            scr_System_CentralControl.current.LLMSetting.currentPromptTemplateId = (id == scr_System_CentralControl.DefaultPresetID ? null : id);
            scr_System_CentralControl.current.StoreLLMSetting();
            panel.LoadPreset(id);
        }
    }

    public class ButtonValidator_AddRootMessage : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        initScript_llmpref_prompt panel;
        public ButtonValidator_AddRootMessage(scr_MenuCanvas_UserPrefs parent, initScript_llmpref_prompt panel) : base(parent)
        {
            this.parent = parent;
            this.panel = panel;
        }

        public override bool IsButtonValid()
        {
            tooltip = "";
            if (!panel.AllowEdit)
            {

                tooltip = "cannot modify default preset, please create new";
                return false;
            }
            return true;
        }

        public void OnClickButton()
        {
            var node = new LLMMessage { role = "system" };
            panel.draft.messages.Add(node);
            panel.AddRootMessageRow(node);
        }
    }

    public class ButtonValidator_DeleteCurrentPreset : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        initScript_llmpref_prompt panel;
        public ButtonValidator_DeleteCurrentPreset(scr_MenuCanvas_UserPrefs parent, initScript_llmpref_prompt panel) : base(parent)
        {
            this.parent = parent;
            this.panel = panel;
        }

        public override bool IsButtonValid()
        {
            tooltip = "";
            if (!panel.AllowEdit)
            {

                tooltip = "cannot delete default preset, please create new";
                return false;
            }
            return true;
        }

        public void OnClickButton()
        {
            if (!panel.editingNewUnsavedPreset)
            {
                scr_System_CentralControl.current.DeletePromptPreset(panel.loadedPresetId);
                panel.RebuildPresetList();
            }
            panel.LoadPreset(null);
        }
    }

    public class ButtonValidator_CreateNewPreset : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        initScript_llmpref_prompt panel;
        public ButtonValidator_CreateNewPreset(scr_MenuCanvas_UserPrefs parent, initScript_llmpref_prompt panel) : base(parent)
        {
            this.parent = parent;
            this.panel = panel;
        }

        public override bool IsButtonValid()
        {
            if (panel.editingNewUnsavedPreset)
            {
                tooltip = "already editing unsaved preset, cannot create new";
                return false;
            }
            tooltip = "";
            return true;
        }

        public void OnClickButton()
        {
            panel.LoadNewUnsavedPreset();
        }
    }

    public class ButtonValidator_SaveCurrentPreset : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        initScript_llmpref_prompt panel;
        public ButtonValidator_SaveCurrentPreset(scr_MenuCanvas_UserPrefs parent, initScript_llmpref_prompt panel) : base(parent)
        {
            this.parent = parent;
            this.panel = panel;
        }

        public override bool IsButtonValid()
        {
            tooltip = "";
            if (!panel.AllowEdit)
            {
                tooltip = "cannot modify default preset, please create new";
                return false; // must Create (or select a player preset) before Save is meaningful
            }
            string name = (panel.draftName ?? "").Trim();
            if (string.IsNullOrEmpty(name) || name == scr_System_CentralControl.DefaultPresetID)
            {
                tooltip = "please rename the preset to a valid unique name";
                return false;
            }
            return true;
        }

        public void OnClickButton()
        {
            string name = panel.draftName.Trim();
            if (panel.loadedPresetId == null || name == panel.loadedPresetId) scr_System_CentralControl.current.SavePromptPreset(name, panel.draft);
            else scr_System_CentralControl.current.RenamePromptPreset(panel.loadedPresetId, name, panel.draft);

            scr_System_CentralControl.current.LLMSetting.currentPromptTemplateId = name;
            scr_System_CentralControl.current.StoreLLMSetting();
            panel.RebuildPresetList();
            panel.LoadPreset(name);
        }
    }
}
