using System;
using System.Collections.Generic;
using System.Text;
using MistEra.Updates;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Updates panel (Plans/GameAndModUpdater.md) and mod manager (Docs/Mods/Plan_ModManagerUI.md). Prefab-driven like the
/// other intro canvases; the fixed scr_SelectableText buttons are matched by optionID (ID_ constants) and relabel
/// themselves from UpdateService state. Two optional row lists: official catalog mods (download / update / redownload /
/// delete) and the mods scanned this boot (load toggle, chosen copy, asset loader options, update, delete). Rows come
/// from prefabs (scr_ModRow) and get optionIDs from DynamicIdStart up. Nothing contacts the network until the player
/// clicks Check (or enabled automatic startup checks). Downloads may run anywhere; installs and deletes of content
/// active this boot only at the title screen. Toggles and options apply at the next boot.
/// </summary>
public class scr_Menu_Updates : scr_Menu
{
    public const int ID_Game = 1;           // update game -> downloading -> click to install
    public const int ID_ModsAll = 2;        // update all mods (x)
    public const int ID_ModsActive = 3;     // update activated mods (x)
    public const int ID_AutomaticChecks = 4;// isButtonToggle
    public const int ID_Check = 5;          // check for updates
    public const int ID_Restart = 6;        // restart game (pending mod changes / restart required)
    public const int ID_Exit = 9999;        // exit / cancel and exit
    const int DynamicIdStart = 10000;       // row buttons
    const float DeleteArmSeconds = 4f;

    static scr_Menu_Updates openMenu;

    UpdateService service;
    public scr_HoverableText text_status;
    public scr_HoverableText text_notice;           // optional: pending changes count, dependency cycle warning
    public RectTransform list_official;             // optional: content container of the official mods list
    public RectTransform list_installed;            // optional: content container of the installed mods list
    public scr_ModRow prefab_modRow_official;
    public scr_ModRow prefab_modRow_installed;
    public scr_SelectableText prefab_modOption;     // isButtonToggle, one per asset loader option
    bool dirty = true;
    float nextRefresh;

    // mod manager rows: official rows are reused by index when the catalog changes; installed rows are built once (boot snapshot)
    readonly List<CatalogRow> catalogRows = new List<CatalogRow>();
    readonly List<ModRowEntry> officialRows = new List<ModRowEntry>();
    readonly List<ModRowEntry> installedRows = new List<ModRowEntry>();
    readonly List<string> dependentsBuffer = new List<string>();
    int nextDynamicId = DynamicIdStart;
    ModRowEntry armedRow;
    bool catalogRowsChanged;

    /// <summary>Label / invalid reason (null = clickable) / tooltip / toggle state of one row button, written by Refresh.</summary>
    class RowSlot
    {
        public string label, reason, tooltip;
        public bool toggled;
        public scr_SelectableText button;
    }

    class OptionSlot : RowSlot
    {
        public AssetLoaderOption option;
    }

    class ModRowEntry
    {
        public scr_ModRow view;
        public CatalogRow catalog;          // official list
        public ModInfo copy;                // installed list: data mod copy
        public bool assetPacks;             // installed list: the single "Assetpacks" row
        public readonly RowSlot main = new RowSlot(), update = new RowSlot(), delete = new RowSlot();
        public readonly List<OptionSlot> options = new List<OptionSlot>();
        public readonly List<int> ids = new List<int>();
        public float armedUntil;
        public string name, nameTooltip, state;
    }

    // one-click update lists, recomputed only when the catalog/receipts change while idle
    readonly List<ModPackage> allUpdates = new List<ModPackage>();
    readonly List<ModPackage> activeUpdates = new List<ModPackage>();
    readonly List<string> skippedUpdates = new List<string>();
    readonly List<string> allConflicts = new List<string>();      // "Mods/<folder>" local copies blocking an update
    readonly List<string> activeConflicts = new List<string>();
    string listsKey;

    // per-button label / invalid reason (null = clickable) / tooltip, written by Refresh, read by the validators
    readonly string[] labels = new string[7];
    readonly string[] reasons = new string[7];
    readonly string[] tooltips = new string[7];
    const int Game = 0, ModsAll = 1, ModsActive = 2, Automatic = 3, Exit = 4, Check = 5, Restart = 6;

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
                case ID_Restart:
                    button.Initialize(this, new ButtonValidator_Update(this, Restart, () => service.RestartGame())); break;
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

        BuildInstalledRows();
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
        // any click other than the armed delete button disarms it
        if (armedRow != null && (armedRow.delete.button == null || armedRow.delete.button.optionID != optionID))
        {
            armedRow.armedUntil = 0;
            armedRow = null;
        }
        if (validator is I_ButtonClickable button) button.OnClickButton();
        // relabel right away (toggles, armed delete); Refresh validates every button
        Refresh();
    }

    void OnChanged() { dirty = true; }

    void Update()
    {
        if (armedRow != null && Time.unscaledTime >= armedRow.armedUntil)
        {
            armedRow.armedUntil = 0;
            armedRow = null;
            dirty = true;
        }
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

        int pending = ModManagerPrefs.PendingCount();
        DescribeRestart(pending);
        DescribeNotice(pending);
        if (catalogRowsChanged) SyncOfficialRows();
        for (int i = 0; i < officialRows.Count; i++) DescribeOfficial(officialRows[i]);
        for (int i = 0; i < installedRows.Count; i++) DescribeInstalled(installedRows[i]);

        SetLabel(ID_Game, Game);
        SetLabel(ID_ModsAll, ModsAll);
        SetLabel(ID_ModsActive, ModsActive);
        SetLabel(ID_AutomaticChecks, Automatic);
        SetLabel(ID_Check, Check);
        SetLabel(ID_Restart, Restart);
        SetLabel(ID_Exit, Exit);
        for (int i = 0; i < officialRows.Count; i++) ApplyRow(officialRows[i]);
        for (int i = 0; i < installedRows.Count; i++) ApplyRow(installedRows[i]);

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
        // catalog rows also give the installed list its plan errors / title rule for Update
        if ((list_official != null && prefab_modRow_official != null) || installedRows.Count > 0)
        {
            service.CollectCatalogRows(catalogRows);
            catalogRowsChanged = true;
        }
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

    // ------------------------------------------------------------------ mod manager: panel-level

    void DescribeRestart(int pending)
    {
        bool anything = pending > 0 || service.RestartRequired || service.ContentAvailableAfterRestart || service.RemovedContentThisBoot;
        labels[Restart] = Text("restart_game");
        reasons[Restart] = Application.isEditor ? Text("restart_editor")
            : !service.BootReady ? Text("tooltip_not_ready")
            : service.IsBusy ? Text("tooltip_busy")
            : !anything ? Text("restart_nothing")
            : !IsIntro() ? Text("restart_title_only")
            : null;
        tooltips[Restart] = pending > 0 ? Text("notice_pending").Replace("$n$", pending.ToString()) : null;
    }

    void DescribeNotice(int pending)
    {
        if (text_notice == null) return;
        var text = new StringBuilder();
        if (pending > 0) text.Append(Text("notice_pending").Replace("$n$", pending.ToString()));
        AppendCycles(text);
        text_notice.SetText(text.ToString());
    }

    /// <summary>One warning line per dependency cycle found at boot (ModLoader.Cycles); also used by the intro entry.</summary>
    public static void AppendCycles(StringBuilder text)
    {
        var cycles = ModLoader.Cycles;
        for (int i = 0; i < cycles.Count; i++) AppendLine(text, Text("notice_cycle").Replace("$cycle$", CycleText(cycles[i])));
    }

    static string CycleText(List<string> cycle) => string.Join(" → ", cycle) + " → " + cycle[0];

    static List<string> FindCycle(string modId)
    {
        var cycles = ModLoader.Cycles;
        for (int i = 0; i < cycles.Count; i++) if (cycles[i].Contains(modId)) return cycles[i];
        return null;
    }

    // ------------------------------------------------------------------ mod manager: rows

    /// <summary>Installed list: every data mod copy scanned this boot (load order, inactive copies after their loaded copy),
    /// then one Assetpacks row. Built once; the boot snapshot does not change while the game runs.</summary>
    void BuildInstalledRows()
    {
        if (list_installed == null || prefab_modRow_installed == null) return;
        var serializer = scr_System_Serializer.current;
        if (serializer == null) return;
        var mods = serializer.Mods;
        var inactive = ModLoader.InactiveCopies;
        for (int i = 0; i < mods.Count; i++)
        {
            AddInstalledRow(mods[i]);
            for (int c = 0; c < inactive.Count; c++)
                if (inactive[c].id == mods[i].id) AddInstalledRow(inactive[c]);
        }
        if (serializer.AssetPacks.Count > 0)
        {
            var entry = CreateRow(prefab_modRow_installed, list_installed);
            entry.assetPacks = true;
            RegisterButton(entry, entry.view.button_main, entry.main, true, null);
            RegisterButton(entry, entry.view.button_update, entry.update, false, null);
            RegisterButton(entry, entry.view.button_delete, entry.delete, false, null);
            installedRows.Add(entry);
        }
    }

    void AddInstalledRow(ModInfo copy)
    {
        var entry = CreateRow(prefab_modRow_installed, list_installed);
        entry.copy = copy;
        var view = entry.view;
        RegisterButton(entry, view.button_main, entry.main, true, () => ModManagerPrefs.SetLoaded(copy, !ModManagerPrefs.IsLoaded(copy)));
        RegisterButton(entry, view.button_update, entry.update, false, () =>
        {
            var receipt = service.ReceiptForFolder(copy.folderName);
            if (receipt != null) service.InstallPackage(receipt.package.id);
        });
        RegisterButton(entry, view.button_delete, entry.delete, true, () => ClickDelete(entry));
        var options = copy.about?.AssetLoaderOptions;
        if (options != null && view.box_options != null && prefab_modOption != null)
        {
            foreach (var o in options)
            {
                if (o == null || string.IsNullOrWhiteSpace(o.key) || o.type != AssetLoaderOption.TypeBoolean) continue;
                bool duplicate = false;
                for (int i = 0; i < entry.options.Count; i++) if (entry.options[i].option.key == o.key) { duplicate = true; break; }
                if (duplicate) continue;   // same filter as ModLoader.ResolveAssetLoaderOptions
                var slot = new OptionSlot { option = o };
                var button = Instantiate(prefab_modOption, view.box_options, false);
                RegisterButton(entry, button, slot, true, () => ModManagerPrefs.SetOption(copy.id, o.key, !ModManagerPrefs.OptionValue(copy.id, o)));
                entry.options.Add(slot);
            }
        }
        installedRows.Add(entry);
    }

    /// <summary>Official list: one row per catalog row, row objects reused by index (no optionID churn on every check).</summary>
    void SyncOfficialRows()
    {
        catalogRowsChanged = false;
        if (list_official == null || prefab_modRow_official == null) return;
        // reused rows show another package now: never keep a delete armed across that
        if (armedRow != null && officialRows.Contains(armedRow))
        {
            armedRow.armedUntil = 0;
            armedRow = null;
        }
        while (officialRows.Count > catalogRows.Count)
        {
            var last = officialRows[officialRows.Count - 1];
            officialRows.RemoveAt(officialRows.Count - 1);
            DestroyRow(last);
        }
        for (int i = 0; i < catalogRows.Count; i++)
        {
            if (i == officialRows.Count)
            {
                var entry = CreateRow(prefab_modRow_official, list_official);
                RegisterButton(entry, entry.view.button_main, entry.main, false, () =>
                {
                    if (entry.catalog != null) service.InstallPackage(entry.catalog.package.id);
                });
                RegisterButton(entry, entry.view.button_delete, entry.delete, true, () => ClickDelete(entry));
                officialRows.Add(entry);
            }
            officialRows[i].catalog = catalogRows[i];
        }
    }

    ModRowEntry CreateRow(scr_ModRow prefab, RectTransform container)
    {
        return new ModRowEntry { view = Instantiate(prefab, container, false) };
    }

    void RegisterButton(ModRowEntry entry, scr_SelectableText button, RowSlot slot, bool toggle, System.Action onClick)
    {
        if (button == null) return;
        slot.button = button;
        if (toggle) button.isButtonToggle = true;
        button.optionID = nextDynamicId++;
        button.Initialize(this, new ButtonValidator_ModRow(this, slot, onClick, toggle ? button : null));
        buttonsByID.Add(button.optionID, button);
        validatorsByID.Add(button.optionID, button.Validator);
        entry.ids.Add(button.optionID);
    }

    void DestroyRow(ModRowEntry entry)
    {
        if (armedRow == entry) armedRow = null;
        for (int i = 0; i < entry.ids.Count; i++)
        {
            buttonsByID.Remove(entry.ids[i]);
            validatorsByID.Remove(entry.ids[i]);
        }
        if (entry.view != null) Destroy(entry.view.gameObject);
    }

    /// <summary>First click arms the row's delete button for DeleteArmSeconds, a second click deletes.</summary>
    void ClickDelete(ModRowEntry entry)
    {
        string id = DeletePackageId(entry);
        if (id == null) return;
        if (armedRow == entry && Time.unscaledTime < entry.armedUntil)
        {
            entry.armedUntil = 0;
            armedRow = null;
            service.DeletePackage(id);
            return;
        }
        entry.armedUntil = Time.unscaledTime + DeleteArmSeconds;
        armedRow = entry;
    }

    string DeletePackageId(ModRowEntry entry)
    {
        if (entry.catalog != null) return entry.catalog.receipt != null ? entry.catalog.receipt.package.id : null;
        if (entry.copy != null)
        {
            var receipt = service.ReceiptForFolder(entry.copy.folderName);
            return receipt != null ? receipt.package.id : null;
        }
        return null;
    }

    CatalogRow FindCatalogRow(string id)
    {
        for (int i = 0; i < catalogRows.Count; i++)
            if (string.Equals(catalogRows[i].package.id, id, StringComparison.OrdinalIgnoreCase)) return catalogRows[i];
        return null;
    }

    string CommonReason()
    {
        return !service.BootReady ? Text("tooltip_not_ready")
            : service.IsBusy ? Text("tooltip_busy")
            : service.RestartRequired ? Text("restart_to_apply")
            : null;
    }

    bool RunningInstall(string id) => id != null && service.IsRunningFor(id) && service.Operation == UpdateOperation.InstallMods;

    // ------------------------------------------------------------------ mod manager: row states

    void DescribeOfficial(ModRowEntry entry)
    {
        var row = entry.catalog;
        if (row == null) return;
        var package = row.package;
        entry.name = Plain(PackageLabel(package)) + "  " + Plain(package.version);
        var details = new StringBuilder();
        string description = CatalogText(package.id, true) ?? package.description;
        if (!string.IsNullOrEmpty(description)) details.Append(Plain(description));
        if (!string.IsNullOrEmpty(package.author)) AppendLine(details, Text("mod_author") + " " + Plain(package.author));
        AppendLine(details, Text("mod_size") + " " + Bytes(package.size));
        entry.nameTooltip = details.ToString();

        switch (row.state)
        {
            case CatalogState.Installed: entry.state = Text(row.active ? "state_installed_loaded" : "state_installed_unloaded"); break;
            case CatalogState.UpdateAvailable:
                entry.state = Text("state_update").Replace("$from$", Plain(row.receipt.package.version)).Replace("$to$", Plain(package.version)); break;
            case CatalogState.Missing: entry.state = Text("state_missing"); break;
            case CatalogState.Occupied: entry.state = Text("state_occupied").Replace("$folder$", Plain(row.occupiedBy)); break;
            case CatalogState.Staged: entry.state = Text("state_staged"); break;
            case CatalogState.InstalledThisBoot: entry.state = Text("state_after_restart"); break;
            case CatalogState.RemovedThisBoot: entry.state = Text("state_deleted"); break;
            default: entry.state = Text("state_not_installed"); break;
        }

        var main = entry.main;
        if (RunningInstall(package.id))
        {
            main.label = Text("mod_downloading") + " " + Percent();
            main.reason = Text("tooltip_busy");
            main.tooltip = null;
        }
        else
        {
            main.label = row.state == CatalogState.UpdateAvailable ? Text("mod_update_to").Replace("$to$", Plain(package.version))
                : row.state == CatalogState.Missing ? Text("mod_redownload")
                : row.state == CatalogState.Installed || row.state == CatalogState.InstalledThisBoot || row.state == CatalogState.Staged ? Text("mod_installed")
                : Text("mod_download");
            main.reason = CommonReason() ?? CatalogReason(row);
            main.tooltip = PlanTooltip(row);
        }
        DescribeDelete(entry, row.receipt, row.active, row.state == CatalogState.Missing, row.state == CatalogState.Staged, package.modId, Text("mod_not_installed"));
    }

    /// <summary>Why Download / Update / Redownload of a catalog row is unavailable (state, plan, title rule), or null.</summary>
    string CatalogReason(CatalogRow row)
    {
        switch (row.state)
        {
            case CatalogState.Installed: return Text("mod_uptodate");
            case CatalogState.Occupied: return Text("mod_occupied").Replace("$folder$", Plain(row.occupiedBy));
            case CatalogState.Staged: return Text("mod_install_pending");
            case CatalogState.InstalledThisBoot:
            case CatalogState.RemovedThisBoot: return Text("restart_to_apply");
        }
        if (row.planError != null) return Plain(row.planError);
        if (row.requiresTitle && !IsIntro()) return Text("title_only");
        return null;
    }

    string PlanTooltip(CatalogRow row)
    {
        if (row == null || row.planError != null) return null;
        if (row.state != CatalogState.NotInstalled && row.state != CatalogState.UpdateAvailable && row.state != CatalogState.Missing) return null;
        var text = new StringBuilder();
        text.Append(Text("mod_download_size")).Append(' ').Append(Bytes(row.downloadSize));
        if (row.batch.Count > 0)
        {
            text.Append('\n').Append(Text("mod_batch"));
            for (int i = 0; i < row.batch.Count; i++) text.Append(i == 0 ? " " : ", ").Append(Plain(row.batch[i]));
        }
        if (row.requiresTitle) text.Append('\n').Append(Text("mod_requires_title"));
        return text.ToString();
    }

    void DescribeDelete(ModRowEntry entry, PackageReceipt receipt, bool active, bool missing, bool staged, string modId, string noReceiptReason)
    {
        var slot = entry.delete;
        bool armed = armedRow == entry && Time.unscaledTime < entry.armedUntil;
        slot.toggled = armed;
        slot.label = Text(armed ? "mod_delete_confirm" : "mod_delete");
        slot.reason = receipt == null ? noReceiptReason
            : CommonReason()
            ?? (staged ? Text("mod_install_pending")
            : active && !IsIntro() ? Text("delete_title_only")
            : null);
        var text = new StringBuilder();
        if (receipt != null)
        {
            if (missing) text.Append(Text("mod_delete_forget"));
            else if (active) text.Append(Text("mod_delete_restart"));
            dependentsBuffer.Clear();
            if (!string.IsNullOrEmpty(modId)) UpdateService.Dependents(modId, dependentsBuffer);
            if (dependentsBuffer.Count > 0) AppendLine(text, Text("mod_delete_dependents").Replace("$mods$", string.Join(", ", dependentsBuffer)));
        }
        slot.tooltip = text.Length > 0 ? text.ToString() : null;
    }

    void DescribeInstalled(ModRowEntry entry)
    {
        if (entry.assetPacks) { DescribeAssetPacks(entry); return; }
        var copy = entry.copy;
        var receipt = service.ReceiptForFolder(copy.folderName);
        string version = copy.about?.version;
        // About name/description: localization keys or plain text of an installed mod, resolved at display time only.
        // An official mod installed this session has no dictionary loaded until restart: the key comes back unresolved,
        // so the catalog's text stands in
        string name = copy.DisplayName;
        string description = copy.Description;
        if (receipt != null && copy.about != null)
        {
            string catalogName = name == copy.about.name ? CatalogText(receipt.package.id, false) : null;
            if (catalogName != null) name = Plain(catalogName);
            string catalogDescription = description == copy.about.description ? CatalogText(receipt.package.id, true) : null;
            if (catalogDescription != null) description = Plain(catalogDescription);
        }
        entry.name = name + (string.IsNullOrEmpty(version) ? "" : "  " + version) + "  " + Text(receipt != null ? "mod_tag_official" : "mod_tag_local");
        var details = new StringBuilder();
        if (!string.IsNullOrEmpty(description)) details.Append(description);
        AppendLine(details, Text("mod_folder").Replace("$folder$", copy.folderName) + "  ModID: " + copy.id);
        if (copy.dependencies.Count > 0) AppendLine(details, Text("mod_dependencies") + " " + string.Join(", ", copy.dependencies));
        entry.nameTooltip = details.ToString();

        bool pending = ModManagerPrefs.IsPending(copy);
        entry.state = !ModManagerPrefs.IsBootChosen(copy) ? Text("state_copy_inactive").Replace("$folder$", copy.chosenFolder)
            : copy.blockedReason != null ? Text("state_blocked") + " " + copy.blockedReason
            : Text(copy.enabled ? "state_loaded" : "state_disabled");
        if (pending) entry.state += "  " + Text("state_pending");

        // load toggle: never invalid, consequences in the tooltip
        var main = entry.main;
        bool loaded = ModManagerPrefs.IsLoaded(copy);
        main.toggled = loaded;
        main.label = Text(loaded ? "mod_enabled" : "mod_disabled");
        main.reason = null;
        var tip = new StringBuilder();
        if (pending) tip.Append(Text("mod_pending"));
        var cycle = FindCycle(copy.id);
        if (cycle != null) AppendLine(tip, Text("mod_cycle").Replace("$cycle$", CycleText(cycle)));
        else if (copy.blockedReason != null) AppendLine(tip, Text("state_blocked") + " " + copy.blockedReason);
        if (loaded)
        {
            dependentsBuffer.Clear();
            UpdateService.Dependents(copy.id, dependentsBuffer);
            if (dependentsBuffer.Count > 0) AppendLine(tip, Text("mod_disable_dependents").Replace("$mods$", string.Join(", ", dependentsBuffer)));
        }
        else if (ModManagerPrefs.CopyCount(copy.id) > 1)
        {
            string chosen = ModManagerPrefs.ChosenFolder(copy.id);
            if (!string.Equals(chosen, copy.folderName, StringComparison.OrdinalIgnoreCase)) AppendLine(tip, Text("mod_copy_switch").Replace("$folder$", chosen));
        }
        main.tooltip = tip.Length > 0 ? tip.ToString() : null;

        // update: official packages only
        var update = entry.update;
        update.tooltip = null;
        string packageId = receipt != null ? receipt.package.id : null;
        ModPackage published = receipt != null ? service.CatalogPackage(packageId) : null;
        bool updateAvailable = published != null && !string.Equals(published.sha256, receipt.package.sha256, StringComparison.OrdinalIgnoreCase);
        if (RunningInstall(packageId))
        {
            update.label = Text("mod_downloading") + " " + Percent();
            update.reason = Text("tooltip_busy");
        }
        else
        {
            update.label = updateAvailable ? Text("mod_update_to").Replace("$to$", Plain(published.version)) : Text("mod_update_button");
            var row = updateAvailable ? FindCatalogRow(packageId) : null;
            update.reason = receipt == null ? Text("mod_local_no_update")
                : CommonReason()
                ?? (!service.Checked ? Text("tooltip_check_first")
                : published == null ? Text("mod_unpublished")
                : service.InstalledThisBoot(packageId) ? Text("restart_to_apply")
                : !updateAvailable ? Text("mod_uptodate")
                : row != null ? CatalogReason(row)
                : null);
            update.tooltip = PlanTooltip(row);
        }

        DescribeDelete(entry, receipt, receipt != null && service.IsPackageActive(receipt.package), receipt != null && service.IsMissing(receipt),
            packageId != null && service.IsStaged(packageId), copy.id, Text("mod_local_delete").Replace("$folder$", copy.folderName));

        for (int i = 0; i < entry.options.Count; i++)
        {
            var slot = entry.options[i];
            var o = slot.option;
            bool value = ModManagerPrefs.OptionValue(copy.id, o);
            string desc = o.Description;
            slot.toggled = value;
            slot.label = (string.IsNullOrEmpty(desc) ? o.key : desc) + ": " + Text(value ? "option_on" : "option_off");
            slot.reason = null;
            slot.tooltip = o.key + (ModManagerPrefs.IsOptionPending(copy, o) ? "\n" + Text("mod_pending") : "");
        }
    }

    void DescribeAssetPacks(ModRowEntry entry)
    {
        var packs = scr_System_Serializer.current.AssetPacks;
        entry.name = Text("mod_assetpacks") + " (" + packs.Count + ")";
        var text = new StringBuilder();
        for (int i = 0; i < packs.Count; i++)
        {
            var pack = packs[i];
            string line = Plain(pack.DisplayName);
            if (pack.DisplayName != pack.id) line += " (" + Plain(pack.id) + ")";
            if (!string.IsNullOrEmpty(pack.about?.author)) line += "  " + Plain(pack.about.author);
            if (!string.IsNullOrEmpty(pack.about?.version)) line += "  " + Plain(pack.about.version);
            AppendLine(text, line);
        }
        entry.nameTooltip = text.ToString();
        entry.state = Text("state_always");
        entry.main.toggled = true;
        entry.main.label = Text("mod_enabled");
        entry.main.reason = Text("mod_assetpacks_fixed");
        entry.main.tooltip = entry.nameTooltip;
        entry.update.label = Text("mod_update_button");
        entry.update.reason = Text("mod_local_no_update");
        entry.delete.label = Text("mod_delete");
        entry.delete.reason = Text("mod_assetpacks_manual");
    }

    void ApplyRow(ModRowEntry entry)
    {
        var view = entry.view;
        if (view == null) return;
        if (view.text_name != null)
        {
            view.text_name.SetText(entry.name);
            view.text_name.SetExternalTooltip(entry.nameTooltip);
        }
        if (view.text_state != null) view.text_state.SetText(entry.state);
        ApplySlot(entry.main);
        ApplySlot(entry.update);
        ApplySlot(entry.delete);
        for (int i = 0; i < entry.options.Count; i++) ApplySlot(entry.options[i]);
        // options only for a copy that is enabled (prefs view), so toggling the mod shows / hides them right away
        if (view.box_options != null && entry.copy != null)
        {
            bool show = entry.main.toggled && entry.options.Count > 0;
            if (view.box_options.gameObject.activeSelf != show) view.box_options.gameObject.SetActive(show);
        }
    }

    static void ApplySlot(RowSlot slot)
    {
        if (slot.button != null && slot.label != null) slot.button.SetText(slot.label);
    }

    static void AppendLine(StringBuilder text, string line)
    {
        if (text.Length > 0) text.Append('\n');
        text.Append(line);
    }

    // ------------------------------------------------------------------ text helpers

    /// <summary>Package name for lists; split asset packs without their own name share the group's, so show the id.</summary>
    static string PackageLabel(ModPackage package)
    {
        if (string.IsNullOrEmpty(package.name)) return package.id;
        if (package.kind == "assetpack" && package.name == package.group && package.id != package.group) return package.id;
        return CatalogText(package.id, false) ?? package.name;
    }

    /// <summary>Package name or description in the current language from catalog-text.json (null when not published).
    /// About name/description are dictionary keys, and a mod's About Dictionary is only loaded once it is installed
    /// and the game restarted. Remote text: callers wrap it in Plain.</summary>
    static string CatalogText(string id, bool description)
    {
        var text = UpdateService.Instance != null ? UpdateService.Instance.CatalogText : null;
        if (text == null || id == null || !text.packages.TryGetValue(id, out var package)) return null;
        var values = description ? package.description : package.name;
        string language = LocalizeDictionary.Instance != null ? LocalizeDictionary.Instance.Index.cachedLang : null;
        if (language != null && values.TryGetValue(language, out string value)) return value;
        return values.TryGetValue("default", out value) ? value : null;
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

    /// <summary>Validator for mod manager row buttons: reads the RowSlot Refresh wrote (same rules as ButtonValidator_Update).</summary>
    class ButtonValidator_ModRow : ButtonValidator, I_ButtonClickable
    {
        readonly RowSlot slot;
        readonly System.Action onClick;
        readonly scr_SelectableText toggleButton;

        public ButtonValidator_ModRow(scr_Menu_Updates parent, RowSlot slot, System.Action onClick, scr_SelectableText toggleButton) : base(parent)
        {
            this.slot = slot;
            this.onClick = onClick;
            this.toggleButton = toggleButton;
        }

        public override bool IsButtonValid()
        {
            if (toggleButton != null) toggleButton.Toggle(true, slot.toggled);
            string reason = slot.reason;
            string extra = slot.tooltip;
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
