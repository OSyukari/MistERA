using System;
using System.Collections.Generic;
using MistEra.Updates;

/// <summary>
/// Mod manager edits of user prefs (ModSettings.ModList): load toggle, chosen copy of a duplicated ModID and asset loader
/// option values. Everything applies at the next boot; the boot snapshot (scr_System_Serializer.Mods,
/// ModLoader.InactiveCopies, ModInfo.toggled/optionValues) stays as scanned, so "pending" = prefs differ from it.
/// See Docs/Mods/Plan_ModManagerUI.md.
/// </summary>
public static class ModManagerPrefs
{
    static readonly List<ModInfo> copiesBuffer = new List<ModInfo>();

    static ModSettingEntry FindEntry(string modId)
    {
        var list = scr_System_CentralControl.current?.ModSetting?.ModList;
        if (list == null) return null;
        foreach (var e in list) if (e != null && e.ModID == modId) return e;
        return null;
    }

    static ModSettingEntry GetOrCreateEntry(string modId)
    {
        var entry = FindEntry(modId);
        if (entry != null) return entry;
        entry = new ModSettingEntry { ModID = modId, enabled = true };
        scr_System_CentralControl.current.ModSetting.ModList.Add(entry);
        return entry;
    }

    /// <summary>Every copy scanned this boot with this ModID (loaded copy + inactive copies), in folder name order. Shared buffer.</summary>
    static List<ModInfo> Copies(string modId)
    {
        copiesBuffer.Clear();
        var serializer = scr_System_Serializer.current;
        if (serializer != null)
            foreach (var m in serializer.Mods) if (m.id == modId) copiesBuffer.Add(m);
        foreach (var m in ModLoader.InactiveCopies) if (m.id == modId) copiesBuffer.Add(m);
        copiesBuffer.Sort((a, b) => string.CompareOrdinal(a.folderName, b.folderName));
        return copiesBuffer;
    }

    /// <summary>Number of copies of this ModID scanned this boot.</summary>
    public static int CopyCount(string modId) => Copies(modId).Count;

    /// <summary>Folder of the copy that loads at next boot by prefs: entry.folder if it is still a copy, else the receipted
    /// copy, else folder name order (same rule as ModLoader.Scan).</summary>
    public static string ChosenFolder(string modId)
    {
        var copies = Copies(modId);
        if (copies.Count == 0) return null;
        string preferred = FindEntry(modId)?.folder;
        if (!string.IsNullOrEmpty(preferred))
            foreach (var c in copies) if (string.Equals(c.folderName, preferred, StringComparison.OrdinalIgnoreCase)) return c.folderName;
        var service = UpdateService.Instance;
        if (service != null)
            foreach (var c in copies) if (service.ReceiptForFolder(c.folderName) != null) return c.folderName;
        return copies[0].folderName;
    }

    /// <summary>Prefs view: this copy loads at next boot (if not blocked).</summary>
    public static bool IsLoaded(ModInfo copy)
    {
        var entry = FindEntry(copy.id);
        bool enabled = entry == null || entry.enabled;  // new mods are registered enabled
        return enabled && string.Equals(ChosenFolder(copy.id), copy.folderName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>on: this copy becomes the chosen one and the ModID is enabled. off: the ModID is disabled (chosen copy kept).</summary>
    public static void SetLoaded(ModInfo copy, bool on)
    {
        var entry = GetOrCreateEntry(copy.id);
        if (on)
        {
            entry.enabled = true;
            if (CopyCount(copy.id) > 1) entry.folder = copy.folderName;
        }
        else entry.enabled = false;
        scr_System_CentralControl.current.SaveUserPref();
    }

    /// <summary>Prefs value of an option (stored "true"/"false", else its default, else true).</summary>
    public static bool OptionValue(string modId, AssetLoaderOption option)
    {
        var entry = FindEntry(modId);
        if (entry?.assetLoaderOptions != null && entry.assetLoaderOptions.TryGetValue(option.key, out var stored) && bool.TryParse(stored, out bool value)) return value;
        return !bool.TryParse(option.defaultValue, out bool defaultValue) || defaultValue;
    }

    public static void SetOption(string modId, string key, bool value)
    {
        var entry = GetOrCreateEntry(modId);
        if (entry.assetLoaderOptions == null) entry.assetLoaderOptions = new Dictionary<string, string>();
        entry.assetLoaderOptions[key] = value ? "true" : "false";
        scr_System_CentralControl.current.SaveUserPref();
    }

    /// <summary>The copy loaded this boot (it is in scr_System_Serializer.Mods; inactive copies are not).</summary>
    public static bool IsBootChosen(ModInfo copy) => copy.chosenFolder == null;

    /// <summary>Option differs from the value applied this boot. Only the boot-chosen copy has applied values.</summary>
    public static bool IsOptionPending(ModInfo copy, AssetLoaderOption option)
    {
        if (!IsBootChosen(copy) || !copy.optionValues.TryGetValue(option.key, out bool boot)) return false;
        return OptionValue(copy.id, option) != boot;
    }

    /// <summary>Prefs differ from this boot for this copy: load toggle, chosen copy, or (boot-chosen copy) an option value.</summary>
    public static bool IsPending(ModInfo copy)
    {
        bool bootLoaded = IsBootChosen(copy) && copy.toggled;
        if (IsLoaded(copy) != bootLoaded) return true;
        var options = copy.about?.AssetLoaderOptions;
        if (options == null) return false;
        foreach (var o in options)
            if (o != null && !string.IsNullOrWhiteSpace(o.key) && o.type == AssetLoaderOption.TypeBoolean && IsOptionPending(copy, o)) return true;
        return false;
    }

    /// <summary>Copies with pending changes (loaded copies + inactive copies of this boot).</summary>
    public static int PendingCount()
    {
        int count = 0;
        var serializer = scr_System_Serializer.current;
        if (serializer != null)
            foreach (var m in serializer.Mods) if (IsPending(m)) count++;
        foreach (var m in ModLoader.InactiveCopies) if (IsPending(m)) count++;
        return count;
    }
}
