using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using TMPro;
using UnityEngine.EventSystems;
using System;

public class initScript_prefs_Display: MonoBehaviour
{
    public TMP_Text text_normal, text_hover, text_conflict, text_disabled, text_maxed, text_toggled;
    public RectTransform selfRect;
    public Image bg_normal, bg_transparent;
    public scr_inputFieldLink input_logs;

    public TMP_Dropdown dropdown_font;
    public string previewSampleText = "AaBb 123 你好世界";

    List<TMP_FontAsset> availableFonts = new List<TMP_FontAsset>();

    public TextMeshProUGUI previewText;

    public void Initialize()
    {
        input_logs.self_inputfield.text = $"{scr_System_CentralControl.current.DisplaySetting.MaxLogCount}";

        previewText.SetText( $"{LocalizeDictionary.QueryThenParse("ui_prefs_textFontPreview")}{previewSampleText}");

        InitializeFontDropdown();
    }

    /// <summary>
    /// Populates only the default option plus the player's current font override (if any) -
    /// deliberately does NOT scan for other available fonts, since that would bake/reconstruct
    /// every ttf for the current language on every settings-panel open. Use the "Refresh Fonts"
    /// button to discover/build the rest on demand.
    /// </summary>
    public void InitializeFontDropdown()
    {
        string lang = scr_System_CentralControl.current.Language;
        availableFonts.Clear();

        string current = scr_System_CentralControl.current.DisplaySetting.FontSelection.TryGetValue(lang, out var name) ? name : "";
        if (!string.IsNullOrEmpty(current))
        {
            var font = scr_System_CentralControl.current.Font; // already-cached/cheap single-font resolution
            if (font != null && font.name == current) availableFonts.Add(font);
            // if it doesn't match (e.g. the ttf was deleted), silently fall back to just the default option
        }

        dropdown_font.gameObject.SetActive(true);
        ApplyDropdownFont(scr_System_CentralControl.current.Font);
        PopulateDropdownOptions(current);
    }

    void PopulateDropdownOptions(string current)
    {
        dropdown_font.ClearOptions();
        var options = new List<TMP_Dropdown.OptionData>();
        options.Add(new TMP_Dropdown.OptionData(LocalizeDictionary.QueryThenParse("ui_prefs_font_default")));
        foreach (var font in availableFonts) options.Add(new TMP_Dropdown.OptionData(font.name));
        dropdown_font.AddOptions(options);

        // index 0 is the "default font" option; availableFonts[i] lives at dropdown index i+1
        int fontIndex = availableFonts.FindIndex(f => f.name == current);
        int selectedIndex = fontIndex >= 0 ? fontIndex + 1 : 0;

        dropdown_font.SetValueWithoutNotify(selectedIndex);
        UpdateFontPreview(selectedIndex);
    }

    bool isRefreshing = false;

    /// <summary>
    /// "Refresh Fonts" button OnClick calls for this. Fonts resolve instantly as on-demand
    /// dynamic assets now, so this is just a re-scan of the FontAssets folder - newly dropped
    /// ttf/otf files appear in the dropdown immediately.
    /// </summary>
    public void OnClickRefreshFonts()
    {
        if (isRefreshing) return;
        StartCoroutine(RefreshFontsCoroutine());
    }

    IEnumerator RefreshFontsCoroutine()
    {
        isRefreshing = true;
        string lang = scr_System_CentralControl.current.Language;

        availableFonts = scr_System_FontManager.GetAvailableFonts(lang);
        string current = scr_System_CentralControl.current.DisplaySetting.FontSelection.TryGetValue(lang, out var name) ? name : "";
        PopulateDropdownOptions(current);
        isRefreshing = false;
        yield break;
    }

    void OnDisable()
    {
        isRefreshing = false;
    }

    void ApplyDropdownFont(TMP_FontAsset font)
    {
        if (font == null) return;

        if (dropdown_font.captionText is TextMeshProUGUI captionText)
        {
            captionText.font = font;
            captionText.UpdateFontAsset();
        }
        if (dropdown_font.itemText is TextMeshProUGUI itemText)
        {
            itemText.font = font;
            itemText.UpdateFontAsset();
        }
    }

    /// <summary>
    /// Dropdown OnValueChanged calls for this
    /// </summary>
    public void OnFontDropdownChanged(int index)
    {
        UpdateFontPreview(index);
    }

    void UpdateFontPreview(int index)
    {
        // valid range is 0 (the "default font" option) through availableFonts.Count (the last real font)
        if (index < 0 || index > availableFonts.Count) return;


        // index 0 is the "default font" option - preview the actual fallback result (which may
        // differ from the currently active font if an override is set right now), since applying
        // it clears the override rather than setting one
        previewText.font = index <= 0
                ? scr_System_CentralControl.current.GetFallbackFont(scr_System_CentralControl.current.Language)
                : availableFonts[index - 1];
        previewText.UpdateFontAsset();
        
    }



    /// <summary>
    /// Font apply button OnClick calls for this
    /// </summary>
    public void ApplySelectedFont()
    {
        int index = dropdown_font.value;
        string lang = scr_System_CentralControl.current.Language;

        if (index <= 0)
        {
            // "default font" option - wipe the saved override so the fallback chain
            // (LanguageFallbackFonts, then DefaultFallbackFont) takes over.
            scr_System_CentralControl.current.SetFontSelection(lang, "");
            scr_System_SceneManager.current.ReloadScene(GlobalValues.IntroScene);
            return;
        }

        int fontIndex = index - 1;
        if (fontIndex < 0 || fontIndex >= availableFonts.Count) return;

        scr_System_CentralControl.current.SetFontSelection(lang, availableFonts[fontIndex].name);
        scr_System_SceneManager.current.ReloadScene(GlobalValues.IntroScene);
    }

    void Start()
    {
        TextColorUpdate();
        BGColorUpdate();
    }

    public void TextColorUpdate()
    {
        text_normal.color = scr_System_CentralControl.current.DisplaySetting.TextColor_neutral.Color;
        text_hover.color = scr_System_CentralControl.current.DisplaySetting.TextColor_hover.Color;
        text_conflict.color = scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color;
        text_disabled.color = scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color;
        text_maxed.color = scr_System_CentralControl.current.DisplaySetting.TextColor_maxed.Color;
        text_toggled.color = scr_System_CentralControl.current.DisplaySetting.TextColor_toggle.Color;
    }

    public void BGColorUpdate()
    {
        bg_normal.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Opaque.Color;
        bg_transparent.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;
    }

    /// <summary>
    /// Inputfield OnValueChanged calls for this
    /// </summary>
    /// <param name="s"></param>
    public void UpdateMaxLogs(string s)
    {
        //Debug.Log($"UpdateMaxLogs");
        if (int.TryParse(input_logs.self_inputfield.text, out int value))
        {
            value = Math.Clamp(value, 0, 150);
            //Debug.Log($"updating maxlogs to {value}");
            scr_System_CentralControl.current.DisplaySetting.MaxLogCount = value;
            input_logs.self_inputfield.text = $"{value}";
        }
    }

    public class ButtonValidator_ApplyFont : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        scr_SelectableText selfButton;
        initScript_prefs_Display display;

        public ButtonValidator_ApplyFont(scr_Menu parent, scr_SelectableText selfButton) : base(parent)
        {
            this.parent = parent as scr_MenuCanvas_UserPrefs;
            this.selfButton = selfButton;
            display = this.parent.initScript_Prefs_Display;
        }

        public override bool IsButtonValid()
        {
            tooltip = "";
            if (display == null) return false;
            if (display.availableFonts.Count <= 0)
            {
                tooltip = "no options available";
                return false;
            }
            return true;
        }

        void I_ButtonClickable.OnClickButton()
        {
            parent.initScript_Prefs_Display.ApplySelectedFont();
            if (display != null && display.dropdown_font != null) display.dropdown_font.value = 0;
        }

    }

    public class ButtonValidator_RefreshFonts : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        scr_SelectableText selfButton;
        initScript_prefs_Display display;

        public ButtonValidator_RefreshFonts(scr_Menu parent, scr_SelectableText selfButton) : base(parent)
        {
            this.parent = parent as scr_MenuCanvas_UserPrefs;
            this.selfButton = selfButton;
            display = this.parent.initScript_Prefs_Display;
        }

        public override bool IsButtonValid()
        {
            tooltip = "";
            if (display == null) return false;
            if (display.isRefreshing)
            {
                tooltip = "refreshing...";
                return false;
            }
            return true;
        }

        void I_ButtonClickable.OnClickButton()
        {
            parent.initScript_Prefs_Display.OnClickRefreshFonts();
        }
    }
}
