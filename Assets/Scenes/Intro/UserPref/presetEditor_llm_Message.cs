using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class presetEditor_llm_Message : MonoBehaviour
{
    public RectTransform selfRect;
    public RectTransform bodyRect;
    public RectTransform childRect;

    public LLMMessage internalNode = null;  // to be set on creation

    public bool allowEdit;  // whether everything (selectable text, inputfield) should be disabled. allowEdit = false when showing the default fallback preset

    public scr_SelectableText btn_roleType; // button validator on click swap role between user system assistant

    public scr_SelectableText btn_nodeType; // button validator that on click changes the node type: Message, _All, _Select. isvalid change the button's name and tooltip to reflect current type
        // if current node has childs, then clicking it only toggles between _all and _select.
        // need to specify in button tooltip that all childs need to be explicitely removed to change its type to standard
    public scr_inputFieldLink tags;

    public scr_inputFieldLink nodeName;  // edits internalNode.name; disabled while folded (only editable when the row is expanded)
    public scr_SelectableText btn_enable;   // toggle node enable or not
    public scr_inputFieldLink text;

    public scr_SelectableText btn_delete;   // delete this node from parent, and unregister every button
    public scr_SelectableText btn_fold;     // hide/display bodyRect

    public scr_SelectableText btn_addChild;

    scr_MenuCanvas_UserPrefs menuParent;
    initScript_llmpref_prompt panel;
    IList<LLMMessage> ownerList;

    List<presetEditor_llm_Message> childNodes = new List<presetEditor_llm_Message>();
    List<int> ownedButtonIDs = new List<int>();
    bool folded = false;

    public void Load(scr_MenuCanvas_UserPrefs menuParent, initScript_llmpref_prompt panel, LLMMessage node, IList<LLMMessage> ownerList, bool allowEdit)
    {
        forbidEditMSG = "cannot modify the default preset, please create new preset";

        this.menuParent = menuParent;
        this.panel = panel;
        this.internalNode = node;
        this.ownerList = ownerList;
        this.allowEdit = allowEdit;

        RegisterOwnedButton(btn_roleType, new ButtonValidator_RoleType(menuParent, this));
        RegisterOwnedButton(btn_nodeType, new ButtonValidator_NodeType(menuParent, this));
        RegisterOwnedButton(btn_enable, new ButtonValidator_ToggleEnable(menuParent, this));
        RegisterOwnedButton(btn_delete, new ButtonValidator_Delete(menuParent, this));
        RegisterOwnedButton(btn_fold, new ButtonValidator_Fold(menuParent, this, btn_fold));
        RegisterOwnedButton(btn_addChild, new ButtonValidator_AddChild(menuParent, this));

        tags.self_inputfield.SetTextWithoutNotify(internalNode.tag ?? "");
        tags.self_inputfield.interactable = allowEdit;
        tags.self_inputfield.onValueChanged.AddListener(s => { internalNode.tag = s; });

        text.self_inputfield.onValueChanged.AddListener(s => { internalNode.content = s; });

        nodeName.self_inputfield.SetTextWithoutNotify(internalNode.name ?? "");
        nodeName.self_inputfield.onValueChanged.AddListener(s => { internalNode.name = s; });
        UpdateNodeNameInteractable();

        RebindLeafFields();

        bool isContainer = GetChildList() != null;
        childRect.gameObject.SetActive(isContainer);
        if (isContainer) BuildChildren();
    }

    void RegisterOwnedButton(scr_SelectableText button, ButtonValidator validator)
    {
        int id = menuParent.AssertUniqueHashPublic(button.GetHashCode());
        button.optionID = id;
        button.Initialize(menuParent, validator);
        menuParent.RegisterButton(id, button, validator);
        ownedButtonIDs.Add(id);
    }

    void BuildChildren()
    {
        var list = GetChildList();
        if (list == null) return;
        foreach (var child in list) InstantiateChildRow(child, list);
    }

    presetEditor_llm_Message InstantiateChildRow(LLMMessage node, IList<LLMMessage> list)
    {
        var row = Instantiate(panel.prefab_presetNode);
        row.transform.SetParent(childRect, false);
        row.Load(menuParent, panel, node, list, allowEdit);
        childNodes.Add(row);
        return row;
    }

    IList<LLMMessage> GetChildList()
    {
        if (internalNode is LLMMessage_All all) return all.contents;
        if (internalNode is LLMMessage_Select sel) return sel.select;
        return null;
    }

    bool HasChildren()
    {
        var list = GetChildList();
        return list != null && list.Count > 0;
    }

    static int TypeIndex(LLMMessage node)
    {
        if (node is LLMMessage_All) return 1;
        if (node is LLMMessage_Select) return 2;
        return 0;
    }

    static readonly string[] RoleCycle = { "system", "user", "assistant" };

    void RebindLeafFields()
    {
        bool isContainer = internalNode is LLMMessage_All || internalNode is LLMMessage_Select;
        text.gameObject.SetActive(!isContainer);
        text.self_inputfield.SetTextWithoutNotify(internalNode.content ?? "");
        text.self_inputfield.interactable = allowEdit;
    }

    // nodeName stays clickable/visible while the row is folded (it's the only way to identify a
    // collapsed node), so its editability has to be gated explicitly rather than via SetActive.
    void UpdateNodeNameInteractable()
    {
        nodeName.self_inputfield.interactable = allowEdit && !folded;
    }

    void RefreshAfterTypeChange()
    {
        foreach (var child in childNodes) child.DestroyAndUnregister();
        childNodes.Clear();

        RebindLeafFields();

        bool isContainer = GetChildList() != null;
        childRect.gameObject.SetActive(isContainer);
        if (isContainer) BuildChildren();
    }

    public void DestroyAndUnregister()
    {
        foreach (var child in childNodes) child.DestroyAndUnregister();
        childNodes.Clear();

        foreach (var id in ownedButtonIDs) menuParent.UnregisterButton(id);
        ownedButtonIDs.Clear();

        Destroy(gameObject);
    }

    public class ButtonValidator_ToggleEnable : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        presetEditor_llm_Message self;
        public ButtonValidator_ToggleEnable(scr_MenuCanvas_UserPrefs parent, presetEditor_llm_Message self) : base(parent)
        {
            this.parent = parent;
            this.self = self;
        }

        public override bool IsButtonValid()
        {
            if (!self.allowEdit)
            {
                tooltip = self.forbidEditMSG;
                return false;
            }
            self.btn_enable.Toggle(true, self.internalNode.enabled);
            tooltip = "";
            return true;
        }

        public void OnClickButton()
        {
            self.internalNode.enabled = !self.internalNode.enabled;
        }
    }

    public class ButtonValidator_Fold : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        presetEditor_llm_Message self;
        scr_SelectableText btn;
        public ButtonValidator_Fold(scr_MenuCanvas_UserPrefs parent, presetEditor_llm_Message self, scr_SelectableText btn) : base(parent)
        {
            this.parent = parent;
            this.self = self;
            this.btn = btn;
        }

        public override bool IsButtonValid()
        {
            btn.SetText(self.folded ? $" + " : " - ");
            return true;
        }

        public void OnClickButton()
        {
            self.folded = !self.folded;
            self.bodyRect.gameObject.SetActive(!self.folded);
            self.UpdateNodeNameInteractable();
        }
    }

    public class ButtonValidator_Delete : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        presetEditor_llm_Message self;
        public ButtonValidator_Delete(scr_MenuCanvas_UserPrefs parent, presetEditor_llm_Message self) : base(parent)
        {
            this.parent = parent;
            this.self = self;
        }

        public override bool IsButtonValid()
        {
            if (!self.allowEdit)
            {
                tooltip = self.forbidEditMSG;
                return false;
            }
            tooltip = "";
            return true;
        }

        public void OnClickButton()
        {
            self.ownerList.Remove(self.internalNode);
            self.DestroyAndUnregister();
        }
    }

    public class ButtonValidator_AddChild : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        presetEditor_llm_Message self;
        public ButtonValidator_AddChild(scr_MenuCanvas_UserPrefs parent, presetEditor_llm_Message self) : base(parent)
        {
            this.parent = parent;
            this.self = self;
        }

        public override bool IsButtonValid()
        {
            if (!self.allowEdit)
            {
                tooltip = self.forbidEditMSG;
                return false;
            }
            if (self.GetChildList() == null)
            {
                tooltip = "cannot add child on a default node, please change node type";
                return false;
            }

            tooltip = "";
            return true;
        }

        public void OnClickButton()
        {
            var list = self.GetChildList();
            if (list == null) return;
            var node = new LLMMessage();
            list.Add(node);
            self.InstantiateChildRow(node, list);
        }
    }

    public class ButtonValidator_RoleType : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        presetEditor_llm_Message self;
        scr_SelectableText button;
        string localizedLabel;

        public ButtonValidator_RoleType(scr_MenuCanvas_UserPrefs parent, presetEditor_llm_Message self) : base(parent)
        {
            this.parent = parent;
            this.self = self;
            this.button = self.btn_roleType;
            this.localizedLabel = LocalizeDictionary.QueryThenParse("ui_prefs_llm_preset_message_roleType");
        }

        static string RoleTypeKey(string role)
        {
            switch (role)
            {
                case "user": return "ui_prefs_llm_preset_roleType_user";
                case "assistant": return "ui_prefs_llm_preset_roleType_assistant";
                default: return "ui_prefs_llm_preset_roleType_system";
            }
        }

        public override bool IsButtonValid()
        {
            if (!self.allowEdit)
            {
                tooltip = self.forbidEditMSG;
                return false;
            }

            string role = self.internalNode.role ?? RoleCycle[0];
            button.SetText(localizedLabel.Replace("$type$", LocalizeDictionary.QueryThenParse(RoleTypeKey(role))));

            tooltip = LocalizeDictionary.QueryThenParse($"\n\n{RoleTypeKey(role)}_tooltip");

            return true;
        }

        public void OnClickButton()
        {
            int idx = System.Array.IndexOf(RoleCycle, self.internalNode.role);
            self.internalNode.role = RoleCycle[(idx + 1) % RoleCycle.Length]; // unrecognized/null role wraps to RoleCycle[0]
        }
    }

    public string forbidEditMSG;

    public class ButtonValidator_NodeType : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        presetEditor_llm_Message self;
        scr_SelectableText button;
        string localizedLabel;

        public ButtonValidator_NodeType(scr_MenuCanvas_UserPrefs parent, presetEditor_llm_Message self) : base(parent)
        {
            this.parent = parent;
            this.self = self;
            this.button = self.btn_nodeType;
            this.localizedLabel = LocalizeDictionary.QueryThenParse("ui_prefs_llm_preset_message_nodeType");
        }

        static string NodeTypeKey(int idx)
        {
            if (idx == 1) return "ui_prefs_llm_preset_nodeType_all";
            if (idx == 2) return "ui_prefs_llm_preset_nodeType_select";
            return "ui_prefs_llm_preset_nodeType_message";
        }

        public override bool IsButtonValid()
        {
            if (!self.allowEdit)
            {
                tooltip = self.forbidEditMSG;
                return false;
            }

            int idx = TypeIndex(self.internalNode);
            button.SetText(localizedLabel.Replace("$type$", LocalizeDictionary.QueryThenParse(NodeTypeKey(idx))));


            tooltip = self.HasChildren()
                ? "Toggles between ALL/SELECT while this node has children. Remove all children to convert back to a plain Message."
                : "Cycles between Message / ALL / SELECT.";

            tooltip += LocalizeDictionary.QueryThenParse($"\n\n{NodeTypeKey(idx)}_tooltip");

            return true;
        }

        public void OnClickButton()
        {
            var node = self.internalNode;
            LLMMessage replacement;

            if (self.HasChildren())
            {
                if (node is LLMMessage_All all)
                {
                    replacement = new LLMMessage_Select { select = new List<LLMMessage>(all.contents) };
                }
                else
                {
                    var sel = (LLMMessage_Select)node;
                    replacement = new LLMMessage_All { contents = new List<LLMMessage>(sel.select) };
                }
            }
            else
            {
                int nextIdx = (TypeIndex(node) + 1) % 3;
                replacement = nextIdx == 0 ? new LLMMessage() : nextIdx == 1 ? (LLMMessage)new LLMMessage_All() : (LLMMessage)new LLMMessage_Select();
            }

            replacement.role = node.role;
            replacement.name = node.name;
            replacement.tag = node.tag;
            replacement.enabled = node.enabled;
            replacement.content = node.content;

            int index = self.ownerList.IndexOf(node);
            self.ownerList[index] = replacement;
            self.internalNode = replacement;

            self.RefreshAfterTypeChange();
        }
    }
}
