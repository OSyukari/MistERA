using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class panel_llm : MonoBehaviour
{
    public RectTransform selfRect;

    // ---- Saved preset list: read-only, create/select/delete only, keys never re-displayed ----

    public RectTransform presetList;
    public scr_LLMPresetRect prefab_presetRect;

    private int presetIdCounter = 5000;
    protected int GetPresetID { get { presetIdCounter++; return presetIdCounter - 1; } }

    protected void BuildPresetButtons()
    {
        foreach (var preset in scr_System_CentralControl.current.LLMSetting.chatCompletionModels)
        {
            AddPresetRow(preset);
        }
    }

    bool initialized = false;

    scr_MenuCanvas_UserPrefs parent = null;
    public void LoadPanel(scr_MenuCanvas_UserPrefs parent)
    {
        if (initialized) return;
        initialized = true;

        this.parent = parent;

        foreach (var text in dropdown_api.options)
        {
            text.text = LocalizeDictionary.QueryThenParse(text.text);
        }
        ResetDraft();
        BuildPresetButtons();

        if (presetList.childCount < 1 || scr_System_CentralControl.current.LLMSetting.currentPresetId == null) NotifyCurrentModel(null);
    }

    public scr_HoverableText ChatCompletionTitle;
    /// <summary>
    /// empty null name means failed
    /// </summary>
    /// <param name="name"></param>
    public void NotifyCurrentModel(string name)
    {
        if (string.IsNullOrEmpty(name)) name = LocalizeDictionary.QueryThenParse("ui_prefs_llm_apipreset_none");
        else name = LocalizeDictionary.QueryThenParse("ui_prefs_llm_apipreset_enabled");

        ChatCompletionTitle.SetText(
            LocalizeDictionary.QueryThenParse("ui_prefs_llm_apisetting_completion_title")
            .Replace("$modelname$", name));
    }

    protected void AddPresetRow(LLM_Setting.ChatCompletion preset)
    {
        scr_LLMPresetRect box = Instantiate(prefab_presetRect);
        box.transform.SetParent(presetList, false);

        box.text_apiType.text = LocalizeDictionary.QueryThenParse($"ui_prefs_llm_apisetting_completion_api_{preset.APIType}");
        box.text_model.text = preset.model;
        box.text_maskedKey.text = MaskKey(preset.key);
        box.text_comment.text = preset.comment;

        scr_SelectableText button1 = box.button_select;
        button1.optionID = GetPresetID * 2;
        button1.Initialize(parent, new ButtonValidator_SelectPreset(parent, preset, button1));
        parent.RegisterButton(button1.optionID, button1, button1.Validator);

        scr_SelectableText button2 = box.button_delete;
        button2.optionID = button1.optionID + 1;
        button2.Initialize(parent, new ButtonValidator_DeletePreset(parent, preset, box));
        parent.RegisterButton(button2.optionID, button2, button2.Validator);
    }

    static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key.Length <= 8) return new string('*', key.Length);
        return $"{key.Substring(0, 4)}...{key.Substring(key.Length - 4, 4)}";
    }

    public RectTransform rect_llm_enabled;
    protected class ButtonValidator_SelectPreset : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        LLM_Setting.ChatCompletion preset;
        scr_SelectableText text;
        bool? lastResult = null;

        Coroutine co = null;

        public ButtonValidator_SelectPreset(scr_MenuCanvas_UserPrefs parent, LLM_Setting.ChatCompletion preset, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.preset = preset;
            this.text = text;
        }
        bool isCurrent = false;
        public override bool IsButtonValid()
        {

            isCurrent = scr_System_CentralControl.current.LLMSetting.currentPresetId == preset.id;
            /// change its logic.
            if (lastResult == null)
            {
                state = ButtonValidator_States.Invalid;
                if (co == null)
                {
                    co = parent.StartCoroutine(parent.panelLLM.GetAvailableModel(preset.modellist, preset.key, (list, req) => OnValidated(list != null)));
                }
                return false;
            }
            else if (lastResult == false)
            {
                state = ButtonValidator_States.Conflict;
                tooltip = "Validation failed: endpoint unreachable or key rejected";


                if (isCurrent)
                {
                    var setting = scr_System_CentralControl.current.LLMSetting;
                    setting.currentPresetId = null;
                    scr_System_CentralControl.current.StoreLLMSetting();
                    parent.panelLLM.NotifyCurrentModel(null);

                }

                return false;
            }
            else
            {
                isCurrent = scr_System_CentralControl.current.LLMSetting.currentPresetId == preset.id;
                text.Toggle(true, isCurrent);

                state = ButtonValidator_States.Valid;
                tooltip = "";

                if (isCurrent)
                {

                    parent.panelLLM.NotifyCurrentModel(preset.id);

                }

                return true;
            }
        }

        void OnValidated(bool success)
        {
            lastResult = success;
            text.Validate();
        }

        public void OnClickButton()
        {
            if (scr_System_CentralControl.current.LLMSetting.currentPresetId == preset.id)
            {
                scr_System_CentralControl.current.LLMSetting.currentPresetId = null;
                scr_System_CentralControl.current.StoreLLMSetting();
                parent.panelLLM.NotifyCurrentModel(null);
            }
            else
            {
                var setting = scr_System_CentralControl.current.LLMSetting;
                setting.currentPresetId = preset.id;
                scr_System_CentralControl.current.StoreLLMSetting();
                parent.panelLLM.NotifyCurrentModel(setting.currentPresetId);
            }
            
        }
    }

    protected class ButtonValidator_DeletePreset : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        LLM_Setting.ChatCompletion preset;
        scr_LLMPresetRect parentRect;

        public ButtonValidator_DeletePreset(scr_MenuCanvas_UserPrefs parent, LLM_Setting.ChatCompletion preset, scr_LLMPresetRect parentRect) : base(parent)
        {
            this.parent = parent;
            this.preset = preset;
            this.parentRect = parentRect;
        }

        public override bool IsButtonValid()
        {
            return scr_System_CentralControl.current.LLMSetting.chatCompletionModels.Contains(preset);
        }

        public void OnClickButton()
        {
            var setting = scr_System_CentralControl.current.LLMSetting;
            setting.chatCompletionModels.Remove(preset);
            if (setting.currentPresetId == preset.id) setting.currentPresetId = null;
            scr_System_CentralControl.current.StoreLLMSetting();
            parentRect.gameObject.SetActive(false);
        }
    }


    // ---- Create-new preset form: edits a local draft only; nothing persists until Save ----

    public RectTransform box_createNew;
    public TMP_InputField comment_custom;
    LLM_Setting.ChatCompletion draftPreset = new LLM_Setting.ChatCompletion();

    void ResetDraft()
    {
        draftPreset = new LLM_Setting.ChatCompletion();
        url_custom.text = "";
        pwd_custom.text = "";
        comment_custom.text = "";
        modelsDropdown.ClearOptions();
        dropdown_api.value = 0;
        errorMSG.SetText("");
        box_createNew.gameObject.SetActive(false);
    }

    public void OnClickCreateNew()
    {
        ResetDraft();
        box_createNew.gameObject.SetActive(true);
    }

    public void OnClickCancelCreate()
    {
        ResetDraft();
    }

    public void OnClickSaveCreate()
    {
        draftPreset.comment = comment_custom.text;
        scr_System_CentralControl.current.LLMSetting.chatCompletionModels.Add(draftPreset);
        scr_System_CentralControl.current.StoreLLMSetting();
        AddPresetRow(draftPreset);
        ResetDraft();
    }

    public class ButtonValidator_SaveCreate : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        public ButtonValidator_SaveCreate(scr_MenuCanvas_UserPrefs parent) : base(parent)
        {
            this.parent = parent;
        }

        public override bool IsButtonValid()
        {
            var d = parent.panelLLM.draftPreset;
            return !string.IsNullOrEmpty(d.endpoint) && !string.IsNullOrEmpty(d.key) && !string.IsNullOrEmpty(d.model);
        }

        public void OnClickButton()
        {
            parent.panelLLM.OnClickSaveCreate();
        }
    }

    public class ButtonValidator_CreateNew : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        scr_SelectableText text;
        public ButtonValidator_CreateNew(scr_MenuCanvas_UserPrefs parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.text = text;
        }
        public override bool IsButtonValid() {

            text.Toggle(true, parent.panelLLM.box_createNew.gameObject.activeInHierarchy);
            return true; 
        
        }
        public void OnClickButton() 
        { 
            if (parent.panelLLM.box_createNew.gameObject.activeInHierarchy) parent.panelLLM.OnClickCancelCreate();
            else parent.panelLLM.OnClickCreateNew(); 
        }
    }

    public class ButtonValidator_CancelCreate : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        public ButtonValidator_CancelCreate(scr_MenuCanvas_UserPrefs parent) : base(parent)
        {
            this.parent = parent;
        }
        public override bool IsButtonValid() { return true; }
        public void OnClickButton() { parent.panelLLM.OnClickCancelCreate(); }
    }


    public TMP_Dropdown dropdown_api;
    public RectTransform box_customAPI;
    public TMP_Text api_title;
    public void OnAPIChange(int i)
    {
        box_customAPI.gameObject.SetActive(i == 0);

        api_title.text = LocalizeDictionary.QueryThenParse($"ui_prefs_llm_apisetting_completion_api_{i}");

        draftPreset.key = pwd_custom.text;
        draftPreset.APIType = i;

        switch (i)
        {
            case 0: // custom endpoint
                OnContentChange_url(url_custom.text);
                break;
            case 1: // google ai studio
                draftPreset.endpoint = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";
                draftPreset.modellist = "https://generativelanguage.googleapis.com/v1beta/openai/models";
                break;
            case 2: // claude anthropic
                draftPreset.endpoint = "https://api.anthropic.com/v1/messages";
                draftPreset.modellist = "https://api.anthropic.com/v1/models";
                break;
            case 3: // openai
                draftPreset.endpoint = "https://api.openai.com/v1/chat/completions";
                draftPreset.modellist = "https://api.openai.com/v1/models";
                break;
            case 4: // z.ai coding plan
                draftPreset.endpoint = "https://api.z.ai/api/coding/paas/v4/chat/completions";
                draftPreset.modellist = "https://api.z.ai/api/coding/paas/v4/models";
                break;
            case 5: // deepseek
                draftPreset.endpoint = "https://api.deepseek.com/chat/completions";
                draftPreset.modellist = "https://api.deepseek.com/models";
                break;
            default:
                break;

        }

        url_custom.text = draftPreset.endpoint;
        pwd_custom.text = draftPreset.key;

        RefreshModels();
    }


    public TMP_InputField url_custom, pwd_custom;
    public void OnContentChange_model(int s)
    {
        draftPreset.model = modelsDropdown.options[s].text;
    }
    public void OnContentChange_url(string s)
    {
        var url = s;
        if (url.Contains("/chat/completions")) url = url.Replace("/chat/completions", "");

        if (draftPreset.APIType == 0)
        {
            draftPreset.endpoint = url;
            if (!draftPreset.endpoint.Contains("/chat/completions")) draftPreset.endpoint += "/chat/completions";
            draftPreset.modellist = url;
            if (!draftPreset.modellist.Contains("/models")) draftPreset.modellist += "/models";
        }
        RefreshModels();
    }
    public void OnContentChange_pwd(string s)
    {
        draftPreset.key = s;
        RefreshModels();
    }

    Coroutine refreshModels = null;

    protected void RefreshModels()
    {
        if (string.IsNullOrEmpty(draftPreset.modellist) || string.IsNullOrEmpty(draftPreset.key)) return;
        if (refreshModels != null)
        {
            StopCoroutine(refreshModels);
            refreshModels = null;
        }
        refreshModels = StartCoroutine(GetAvailableModel(draftPreset.modellist, draftPreset.key, OnModelFound));
    }

    protected void OnModelFound(ModelList model, UnityWebRequest request)
    {
        if (model == null)
        {
            errorMSG.SetText(Utility.WrapTextColor($"Failed to fetch models: {request.error}", scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color));
            return;
        }
        else
        {
            errorMSG.SetText("");
        }

        string existing = draftPreset.model;
        int newvalue = 0;

        List<string> modelnames = new List<string>();

        for (int i = 0; i < model.data.Count; i++)
        {
            modelnames.Add(model.data[i].id);
            if (model.data[i].id == existing) newvalue = i;
        }
        //Debug.Log($"Found {model.data.Count} models: {String.Join(" ", modelnames)}");

        modelsDropdown.ClearOptions();
        modelsDropdown.AddOptions(modelnames);
        modelsDropdown.value = newvalue;

        OnContentChange_model(newvalue);

        this.parent.ValidateAll();

    }
    public scr_HoverableText errorMSG;

    public TMP_Dropdown modelsDropdown;

    [Serializable] public class ModelList { public List<ModelData> data; }
    [Serializable] public class ModelData { public string id; }
    /// <summary>
    /// Fetches available models from a custom URL and returns the first ID found.
    /// </summary>
    public IEnumerator GetAvailableModel(string baseUrl, string apiKey, Action<ModelList, UnityWebRequest> onModelFound)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(baseUrl))
        {

            if (baseUrl.Contains("anthropic"))
            {
                request.SetRequestHeader("x-api-key", apiKey);
                request.SetRequestHeader("anthropic-version", "2023-06-01");
                //request.SetRequestHeader("Accept", "application/json");
            }
            else
            {
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            }

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                // Simple JSON parsing (Consider using a proper JSON library like Newtonsoft for complex nested objects)
                ModelList list = JsonUtility.FromJson<ModelList>(request.downloadHandler.text);
                if (list != null && list.data.Count > 0)
                {
                    onModelFound?.Invoke(list, request);
                }
            }
            else
            {
                onModelFound?.Invoke(null, request);
            }
        }
    }
}
