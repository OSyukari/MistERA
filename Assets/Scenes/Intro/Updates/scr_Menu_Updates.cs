using System;
using System.Collections.Generic;
using System.Text;
using MistEra.Updates;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Updates panel (Plans/GameAndModUpdater.md): a lean update manager, not a mod manager. Prefab-driven like the
/// other intro canvases; the fixed scr_SelectableText buttons are matched by optionID (ID_ constants) and relabel
/// themselves from UpdateService state. Nothing contacts the network until the player clicks Check (or enabled
/// automatic startup checks). Downloads may run anywhere; installs (game restart, mod files) only at the title screen.
/// </summary>
public class scr_Menu_Updates : scr_Menu
{
    public const int ID_Game = 1;           // update game -> downloading -> click to install
    public const int ID_ModsAll = 2;        // update all mods (x)
    public const int ID_ModsActive = 3;     // update activated mods (x)
    public const int ID_AutomaticChecks = 4;// isButtonToggle
    public const int ID_Check = 5;          // check for updates
    public const int ID_Exit = 9999;        // exit / cancel and exit

    static scr_Menu_Updates openMenu;

    UpdateService service;
    public scr_HoverableText text_status;
    bool dirty = true;
    float nextRefresh;

    // one-click update lists, recomputed only when the catalog/receipts change while idle
    readonly List<ModPackage> allUpdates = new List<ModPackage>();
    readonly List<ModPackage> activeUpdates = new List<ModPackage>();
    readonly List<string> skippedUpdates = new List<string>();
    readonly List<string> allConflicts = new List<string>();      // "Mods/<folder>" local copies blocking an update
    readonly List<string> activeConflicts = new List<string>();
    string listsKey;

    // per-button label / invalid reason (null = clickable) / tooltip, written by Refresh, read by the validators
    readonly string[] labels = new string[6];
    readonly string[] reasons = new string[6];
    readonly string[] tooltips = new string[6];
    const int Game = 0, ModsAll = 1, ModsActive = 2, Automatic = 3, Exit = 4, Check = 5;

    public static string Text(string key)
    {
        return LocalizeDictionary.Instance != null ? LocalizeDictionary.QueryThenParse("updates_" + key) : key;
    }

    /// <summary>Only active-content commits, restart handoff and a pending restart block starting or loading a game.</summary>
    public static bool GameplayBlocked
    {
        get { return UpdateService.Instance != null && UpdateService.Instance.BlocksGameplay; }
    }

    /// <summary>Opens the panel over parent, once.</summary>
    public static void Open(RectTransform prefab, RectTransform parent)
    {
        if (openMenu != null) return;
        if (prefab == null)
        {
            Debug.LogError("scr_Menu_Updates: prefab_menuCanvas_Updates is not assigned");
            return;
        }
        UpdateService.EnsureCreated();
        scr_System_SceneManager.current.LoadCanvasIntoScene(prefab, parent);
    }

    protected override void Awake()
    {
        base.Awake();
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
    }

    public override void Initialize()
    {
        base.Initialize();
        openMenu = this;
        service = UpdateService.EnsureCreated();
        service.Changed += OnChanged;

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            switch (button.optionID)
            {
                case ID_Game:
                    button.Initialize(this, new ButtonValidator_Update(this, Game, ClickGame)); break;
                case ID_ModsAll:
                    button.Initialize(this, new ButtonValidator_Update(this, ModsAll, () => service.InstallPackages(new List<ModPackage>(allUpdates), ModUpdateScope.All))); break;
                case ID_ModsActive:
                    button.Initialize(this, new ButtonValidator_Update(this, ModsActive, () => service.InstallPackages(new List<ModPackage>(activeUpdates), ModUpdateScope.Active))); break;
                case ID_AutomaticChecks:
                    button.Initialize(this, new ButtonValidator_Update(this, Automatic, () => service.SetAutomaticChecks(!service.AutomaticChecks),
                        button, () => service.AutomaticChecks)); break;
                case ID_Check:
                    button.Initialize(this, new ButtonValidator_Update(this, Check, () => service.CheckForUpdates())); break;
                case ID_Exit:
                    button.Initialize(this, new ButtonValidator_Update(this, Exit, ClickExit)); break;
                case -1:
                    break;
                default:
                    button.Initialize(this, button_alwaysValid); break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }
        }

        Refresh();
    }

    protected override void OnDestroy()
    {
        if (service != null) service.Changed -= OnChanged;
        if (openMenu == this) openMenu = null;
        base.OnDestroy();
    }

    public override void Notify(int optionID)
    {
        if (!validatorsByID.TryGetValue(optionID, out ButtonValidator validator)) return;
        if (validator is I_ButtonClickable button) button.OnClickButton();
        dirty = true;
        if (!validator.noValidate) ValidateAll();
    }

    void OnChanged() { dirty = true; }

    void Update()
    {
        if (!initialized || !dirty || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.2f;
        Refresh();
    }

    // ------------------------------------------------------------------ clicks

    void ClickGame()
    {
        if (service.HasPendingBuild) service.RestartToUpdate();
        else service.DownloadBuild();
    }

    void ClickExit()
    {
        // closing cancels a running check or download; installs and restart handoff keep the panel open
        if (service.IsBusy) service.Cancel();
        scr_System_SceneManager.current.UnloadLastCanvasFromScene(typeof(scr_Menu_Updates));
    }

    // ------------------------------------------------------------------ refresh

    void Refresh()
    {
        dirty = false;
        if (service == null) return;

        RefreshLists();
        DescribeGame();
        DescribeMods(ModsAll, ModUpdateScope.All, allUpdates, allConflicts, "mods_all", "mods_all_done");
        DescribeMods(ModsActive, ModUpdateScope.Active, activeUpdates, activeConflicts, "mods_active", "mods_active_done");

        labels[Check] = Text(service.Operation == UpdateOperation.Check ? "checking" : "check");
        reasons[Check] = !service.BootReady ? Text("tooltip_not_ready") : service.IsBusy ? Text("tooltip_busy") : null;
        tooltips[Check] = Text("consent");

        labels[Automatic] = Text("automatic");
        reasons[Automatic] = service.BootReady ? null : Text("tooltip_not_ready");
        tooltips[Automatic] = Text("consent");

        bool cancellable = service.IsBusy && !service.BlocksGameplay;
        labels[Exit] = Text(cancellable ? "cancel_exit" : "exit");
        reasons[Exit] = service.IsBusy && service.BlocksGameplay ? Text("tooltip_close_busy") : null;
        tooltips[Exit] = null;

        SetLabel(ID_Game, Game);
        SetLabel(ID_ModsAll, ModsAll);
        SetLabel(ID_ModsActive, ModsActive);
        SetLabel(ID_AutomaticChecks, Automatic);
        SetLabel(ID_Check, Check);
        SetLabel(ID_Exit, Exit);

        if (text_status != null)
        {
            string status = Plain(service.Status);
            if (service.IsBusy) status += "  " + Percent();
            if (!string.IsNullOrEmpty(service.Error)) status += "\n" + Plain(service.Error);
            text_status.SetText(status);
        }
        ValidateAll();
    }

    void SetLabel(int optionID, int slot)
    {
        if (buttonsByID.TryGetValue(optionID, out scr_SelectableText button)) button.SetText(labels[slot]);
    }

    void RefreshLists()
    {
        if (service.IsBusy) return;
        string key = service.InventoryVersion + "|" + service.RestartRequired + "|" + service.CompletedModUpdate;
        if (key == listsKey) return;
        listsKey = key;
        skippedUpdates.Clear();
        allConflicts.Clear();
        activeConflicts.Clear();
        service.CollectModUpdates(false, allUpdates, skippedUpdates, allConflicts);
        service.CollectModUpdates(true, activeUpdates, null, activeConflicts);
    }

    void DescribeGame()
    {
        tooltips[Game] = null;
        var op = service.Operation;
        if (!service.BootReady)
        {
            labels[Game] = Text("game_update");
            reasons[Game] = Text("tooltip_not_ready");
        }
        else if (op == UpdateOperation.DownloadBuild || op == UpdateOperation.Restart)
        {
            // each stage reports its own 0-100%: name the stage so a restarting percent never reads as a stalled download
            string stage = service.Step == BuildStep.Verifying ? "game_verifying"
                : service.Step == BuildStep.Extracting ? "game_extracting"
                : op == UpdateOperation.Restart ? "game_preparing" : "game_downloading";
            labels[Game] = Text(stage) + (op == UpdateOperation.Restart ? "" : " " + Percent());
            reasons[Game] = Text("tooltip_busy");
        }
        else if (service.HasPendingBuild)
        {
            labels[Game] = Text("game_install");
            reasons[Game] = service.IsBusy ? Text("tooltip_busy") : !IsIntro() ? Text("title_only") : null;
            tooltips[Game] = BuildDescription(service.InstalledBuild) + " → " + ReleaseDescription();
        }
        else if (!service.Checked)
        {
            labels[Game] = Text("game_update");
            reasons[Game] = service.IsBusy ? Text("tooltip_busy") : Text("tooltip_check_first");
        }
        else if (service.AvailableBuild != null)
        {
            labels[Game] = Text("game_update") + " " + Plain(service.AvailableBuild.version);
            reasons[Game] = service.IsBusy ? Text("tooltip_busy") : null;
            tooltips[Game] = BuildDescription(service.InstalledBuild) + " → " + ReleaseDescription();
        }
        else
        {
            labels[Game] = Text("game_latest");
            reasons[Game] = Text("game_latest");
            tooltips[Game] = BuildDescription(service.InstalledBuild);
        }
    }

    void DescribeMods(int slot, ModUpdateScope scope, List<ModPackage> updates, List<string> conflicts, string labelKey, string doneKey)
    {
        string list = UpdateList(updates, conflicts, slot == ModsAll);
        if (service.RunningModUpdate == scope)
        {
            labels[slot] = Text("mods_updating") + " " + Percent();
            reasons[slot] = Text("tooltip_busy");
        }
        else if (service.CompletedModUpdate == scope)
        {
            labels[slot] = Text(doneKey);
            reasons[slot] = Text("restart_to_apply");
        }
        else
        {
            labels[slot] = Text(labelKey) + " (" + updates.Count +
                (conflicts.Count > 0 ? ", " + Text("mods_conflicts") + " " + conflicts.Count : "") + ")";
            reasons[slot] = !service.BootReady ? Text("tooltip_not_ready")
                : !service.Checked ? Text("tooltip_check_first")
                : service.IsBusy ? Text("tooltip_busy")
                : service.RestartRequired || service.CompletedModUpdate != ModUpdateScope.None ? Text("restart_to_apply")
                : !IsIntro() ? Text("title_only")
                : updates.Count == 0 ? Text("mods_none")
                : null;
        }
        tooltips[slot] = list;
    }

    /// <summary>Tooltip list: one line per package (new / update), then local copies blocking an update, then packages left out and why.</summary>
    string UpdateList(List<ModPackage> updates, List<string> conflicts, bool withSkipped)
    {
        var receipts = service.InstalledPackages;
        var text = new StringBuilder();
        for (int i = 0; i < updates.Count; i++)
        {
            var package = updates[i];
            PackageReceipt receipt = null;
            for (int r = 0; r < receipts.Count; r++)
                if (string.Equals(receipts[r].package.id, package.id, StringComparison.OrdinalIgnoreCase)) { receipt = receipts[r]; break; }
            if (text.Length > 0) text.Append('\n');
            text.Append(Text(receipt == null ? "mod_new" : "mod_update")).Append(' ').Append(Plain(PackageLabel(package)));
            if (receipt != null && receipt.package.version != package.version)
                text.Append("  ").Append(Plain(receipt.package.version)).Append(" → ").Append(Plain(package.version));
            else text.Append("  ").Append(Plain(package.version));
        }
        for (int i = 0; i < conflicts.Count; i++)
        {
            if (text.Length > 0) text.Append('\n');
            text.Append(Text("mod_conflict")).Append(' ').Append(Plain(conflicts[i])).Append(' ').Append(Text("mod_conflict_local"));
        }
        if (withSkipped)
            for (int i = 0; i < skippedUpdates.Count; i++)
            {
                if (text.Length > 0) text.Append('\n');
                text.Append(Text("mod_skipped")).Append(' ').Append(Plain(skippedUpdates[i]));
            }
        return text.Length > 0 ? text.ToString() : null;
    }

    // ------------------------------------------------------------------ text helpers

    /// <summary>Package name for lists; split asset packs without their own name share the group's, so show the id.</summary>
    static string PackageLabel(ModPackage package)
    {
        if (string.IsNullOrEmpty(package.name)) return package.id;
        if (package.kind == "assetpack" && package.name == package.group && package.id != package.group) return package.id;
        return package.name;
    }

    string Percent()
    {
        float progress = float.IsNaN(service.Progress) ? 0 : Mathf.Clamp01(service.Progress);
        return progress.ToString("P0");
    }

    static string BuildDescription(BuildInfo build)
    {
        return build == null ? Text("unknown") : Plain(build.version) + " (" + Plain(build.channel) + ")";
    }

    string ReleaseDescription()
    {
        return service.AvailableBuild == null ? Text("unknown") : BuildDescription(service.AvailableBuild) + " " + Bytes(service.AvailableBuild.size);
    }

    /// <summary>Catalog, release and error text is untrusted: shown verbatim, never parsed as TMP markup.</summary>
    static string Plain(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return "<noparse>" + value.Replace("</noparse>", "", StringComparison.OrdinalIgnoreCase) + "</noparse>";
    }

    static string Bytes(long value)
    {
        if (value < 0) return "?";
        if (value >= 1024L * 1024 * 1024) return (value / (1024d * 1024 * 1024)).ToString("F2") + " GiB";
        if (value >= 1024 * 1024) return (value / (1024d * 1024)).ToString("F1") + " MiB";
        if (value >= 1024) return (value / 1024d).ToString("F1") + " KiB";
        return value + " B";
    }

    static bool IsIntro()
    {
        return SceneManager.GetSceneByPath(GlobalValues.IntroScene).isLoaded &&
            !SceneManager.GetSceneByPath(GlobalValues.GameScene).isLoaded &&
            scr_System_SceneManager.current != null && !scr_System_SceneManager.current.SceneLoading;
    }

    /// <summary>
    /// Validator for every updater button: reads the label slot Refresh wrote. Invalid buttons stay visible with
    /// their reason (plus the update list for mod buttons) as tooltip. Toggle buttons pass their button and state.
    /// </summary>
    class ButtonValidator_Update : ButtonValidator, I_ButtonClickable
    {
        new readonly scr_Menu_Updates parent;
        readonly int slot;
        readonly System.Action onClick;
        readonly scr_SelectableText toggleButton;
        readonly Func<bool> toggled;

        public ButtonValidator_Update(scr_Menu_Updates parent, int slot, System.Action onClick,
            scr_SelectableText toggleButton = null, Func<bool> toggled = null) : base(parent)
        {
            this.parent = parent;
            this.slot = slot;
            this.onClick = onClick;
            this.toggleButton = toggleButton;
            this.toggled = toggled;
        }

        public override bool IsButtonValid()
        {
            if (toggleButton != null && toggled != null) toggleButton.Toggle(true, toggled());
            string reason = parent.reasons[slot];
            string extra = parent.tooltips[slot];
            tooltip = reason == null ? extra ?? "" : string.IsNullOrEmpty(extra) ? reason : reason + "\n\n" + extra;
            state = reason == null ? ButtonValidator_States.Valid : ButtonValidator_States.Invalid;
            return reason == null;
        }

        public void OnClickButton()
        {
            try { onClick?.Invoke(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }
}
