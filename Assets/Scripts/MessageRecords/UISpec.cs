using System.Collections.Generic;
using Newtonsoft.Json;

public enum LogsDisplayMode
{
    Dontcare,
    ERA,
    AVG
}

public enum InfoTabDisplayMode
{
    Dontcare,
    Display,
    Hide
}

/// <summary>
/// Consolidated UI-display settings for an event entry / message log line: who to show a portrait
/// of, which mood tags to use, and which background image to show. Held as a single field on both
/// Event.EventEntry (authored in JSON) and MessageLog (runtime), instead of loose sibling fields,
/// so new UI-related settings only need to be added in one place.
/// </summary>
public class UISpec
{
    [JsonProperty("portraitRefKey")] public string PortraitRefKey = "";
    [JsonProperty("selfTags")] public List<string> SelfTags = new List<string>();
    [JsonProperty("targetTags")] public List<string> TargetTags = new List<string>();

    [JsonIgnore] public PortraitManager PortraitRef = null;
    [JsonIgnore] public List<Character_Trainable> MultipleChara = new List<Character_Trainable>();

    public UISpec Clone()
    {
        return new UISpec
        {
            PortraitRefKey = PortraitRefKey,
            SelfTags = new List<string>(SelfTags),
            TargetTags = new List<string>(TargetTags),
            BGImagePath = BGImagePath,
            displayMode = displayMode,
            infoDisplay = infoDisplay,
            PortraitRef = PortraitRef,
            MultipleChara = new List<Character_Trainable>(MultipleChara)
        };
    }

    /// <summary>
    /// A UISpec suitable for authoring (Event.UISpec / EventEntry.UISpec): displayMode/infoDisplay
    /// default to Dontcare here (instead of the base class's ERA/Display), so an unauthored field is
    /// distinguishable from an explicit choice and won't stomp a sticky setting via Overwrite/PersistInto.
    /// Newtonsoft's default ObjectCreationHandling.Auto populates matched JSON properties onto this
    /// pre-built instance, so only fields actually present in JSON overwrite these defaults.
    /// </summary>
    public static UISpec Template()
    {
        return new UISpec
        {
            displayMode = LogsDisplayMode.Dontcare,
            infoDisplay = InfoTabDisplayMode.Dontcare
        };
    }

    /// <summary>
    /// Returns a copy of this UISpec (the template/default) with each non-default field of
    /// `overrides` applied on top. Lets a child entry author only the fields it wants to change,
    /// inheriting the rest from the template instead of repeating it.
    /// </summary>
    public UISpec Overwrite(UISpec overrides)
    {
        var result = Clone();
        if (overrides == null) return result;
        if (overrides.PortraitRefKey != "") result.PortraitRefKey = overrides.PortraitRefKey;
        if (overrides.SelfTags.Count > 0) result.SelfTags = new List<string>(overrides.SelfTags);
        if (overrides.TargetTags.Count > 0) result.TargetTags = new List<string>(overrides.TargetTags);
        if (overrides.BGImagePath != "") result.BGImagePath = overrides.BGImagePath;
        if (overrides.displayMode != LogsDisplayMode.Dontcare) result.displayMode = overrides.displayMode;
        if (overrides.infoDisplay != InfoTabDisplayMode.Dontcare) result.infoDisplay = overrides.infoDisplay;
        return result;
    }

    /// <summary>
    /// Copies this UISpec's non-default "sticky" fields into `registry`, so an entry's own override
    /// also becomes the running default for every following entry until changed again - rather than
    /// applying only to the one entry that authored it (see Overwrite). Only fields that make sense to
    /// carry forward across lines are sticky (BGImagePath, displayMode, infoDisplay); portrait/tags are
    /// per-line by nature (a different character can speak on the very next line) and are intentionally
    /// excluded.
    /// </summary>
    public void PersistInto(UISpec registry)
    {
        if (registry == null) return;
        if (BGImagePath != "") registry.BGImagePath = BGImagePath;
        if (displayMode != LogsDisplayMode.Dontcare) registry.displayMode = displayMode;
        if (infoDisplay != InfoTabDisplayMode.Dontcare) registry.infoDisplay = infoDisplay;
    }


    // -- below are persist data -- //

    /// <summary>
    /// Background image path. Seeded per-event from Event.UISpec (the event's template/default
    /// display setting) and mutated at runtime by the SetBGImage result; see EventInstance.CurrentUISpec.
    /// </summary>
    [JsonProperty("bgImagePath")] public string BGImagePath = "";

    /// <summary>
    /// if dontcare, then dont overwrite existing
    /// if era, then print message in list
    /// if AVG, then use the "only display one message at a time" system, and disable bgdarkcover
    /// </summary>
    public LogsDisplayMode displayMode = LogsDisplayMode.ERA;
    /// <summary>
    /// if hide, then set alpha 0 on dayinfo and topbar,
    /// </summary>
    public InfoTabDisplayMode infoDisplay = InfoTabDisplayMode.Display;

    // these are default values.
    // in an event, the event can have dontcare as default that get overwritten by specific settings,
    // but when event msg ends and other msgs (from other updates, from kojo, from time tick),
    // the ui need to go back to normal.
}
