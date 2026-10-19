using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;


public class PortraitManager
{
    /// <summary>
    /// Portrait handlers by priority. If serializedData exists (template data, player-customized portraits, legacy saves) it is returned as-is.
    /// Otherwise lazily copied from the base character template portraitBaseIDOverride into _portraitCache, so un-customized characters don't save the full list.
    /// </summary>
    [JsonIgnore] public List<CharaPortrait> portraitPriorityList
    {
        get
        {
            if (serializedData != null) return serializedData;
            if (_portraitCache == null)
            {
                _portraitCache = new List<CharaPortrait>();
                var template = CacheTemplate;
                if (template != null && template.Portrait != null)
                {
                    foreach (var pp in template.Portrait.portraitPriorityList)
                    {
                        var copy = pp.Copy();
                        if (copy == null) continue;
                        copy.Owner = this;
                        copy.RebuildInternal();
                        _portraitCache.Add(copy);
                    }
                }
            }
            return _portraitCache;
        }
    }
    protected List<CharaPortrait> _portraitCache = null;
    [JsonProperty("portraitPriorityList")] protected List<CharaPortrait> serializedData = null;

    /// <summary>
    /// Banners are not customizable: resolved from the portraitBaseIDOverride template unless serializedBanner exists (template data, legacy saves)
    /// </summary>
    [JsonIgnore] public CharaPortrait CharaBanner
    {
        get
        {
            if (serializedBanner != null) return serializedBanner;
            if (!_bannerCacheInit)
            {
                _bannerCacheInit = true;
                var template = CacheTemplate;
                if (template != null && template.Portrait != null && template.Portrait.CharaBanner != null)
                {
                    _bannerCache = template.Portrait.CharaBanner.Copy();
                    if (_bannerCache != null)
                    {
                        _bannerCache.Owner = this;
                        _bannerCache.RebuildInternal();
                    }
                }
            }
            return _bannerCache;
        }
    }
    [JsonIgnore] public string CharaBannerBGColor
    {
        get
        {
            if (serializedBannerBGColor != null) return serializedBannerBGColor;
            var template = CacheTemplate;
            if (template != null && template.Portrait != null) return template.Portrait.CharaBannerBGColor;
            return "";
        }
    }
    protected CharaPortrait _bannerCache = null;
    protected bool _bannerCacheInit = false;
    [JsonProperty("CharaBanner")] protected CharaPortrait serializedBanner = null;
    [JsonProperty("CharaBannerBGColor")] protected string serializedBannerBGColor = null;

    /// <summary>
    /// Cache source - explicit portraitBaseIDOverride only, no BaseID fallback (does not depend on Owner)
    /// </summary>
    protected Character_SerializableBase CacheTemplate
    {
        get
        {
            if (string.IsNullOrEmpty(portraitBaseIDOverride)) return null;
            return scr_System_Serializer.current.MasterList.Character_Bases.GetByID(portraitBaseIDOverride);
        }
    }

    /// <summary>
    /// Switch to cache mode: wipe any serialized portrait/banner data and resolve from base character [baseID] instead
    /// </summary>
    public void InitFromTemplate(string baseID)
    {
        serializedData = null;
        serializedBanner = null;
        serializedBannerBGColor = null;
        _portraitCache = null;
        ResetGeneratedAssetHandlers();
        _bannerCache = null;
        _bannerCacheInit = false;
        colorInit = false;
        portraitBaseIDOverride = baseID;
    }

    /// <summary>
    /// Call before modifying portrait handlers - moves the cached handlers into serializedData so the modification is saved
    /// </summary>
    public void MarkCustomized()
    {
        if (serializedData != null) return;
        serializedData = portraitPriorityList;
    }

    Color _CharaBannerBG;
    bool colorInit = false;

    public void SetBGColor(UnityEngine.UI.Image image)
    {
        if (image == null) return;
        var bgColor = CharaBannerBGColor;
        if (string.IsNullOrEmpty(bgColor)) return;
        if (!colorInit)
        {
            _CharaBannerBG = UtilityEX.ColorFromHex(bgColor);
            colorInit = true;
        }
        image.color = _CharaBannerBG;
    }

    public string portraitBaseIDOverride = "";

    /// <summary>
    /// One source of generated handlers (GlobalPortraitTemplate): a character (variant) folder whose category folders hold the classified images
    /// </summary>
    public class GlobalHandlerEntry
    {
        /// <summary>
        /// The folder holding the category folders: its name (eg. "Aemeath") or a longer path ending in it (eg. "cinderella/cinderella_after",
        /// matched against the file's full path, so a same-named folder elsewhere is not picked up)
        /// </summary>
        public string path = "";
        /// <summary> added to RequireSelfContextKeys of every handler generated from this folder (eg. ["corrupted"]) </summary>
        public List<string> selfKeys = new List<string>();
    }

    /// <summary>
    /// Character template data only: global portrait template sources in priority order (all handlers of the first entry, then the next...).
    /// Instances read it from their portrait template and never store it.
    /// </summary>
    [JsonIgnore] public List<GlobalHandlerEntry> useGlobalHandler = null;

    /// <summary>
    /// Read-only JSON hook for useGlobalHandler: no getter, so it is never written (never in save files). Values that are not an array
    /// (eg. the legacy string an old save stored on an instance) are ignored; instances never use their own value anyway (GlobalHandlerEntries)
    /// </summary>
    [JsonProperty("useGlobalHandler")] protected Newtonsoft.Json.Linq.JToken UseGlobalHandlerJson
    {
        set { useGlobalHandler = value is Newtonsoft.Json.Linq.JArray a ? a.ToObject<List<GlobalHandlerEntry>>() : null; }
    }

    static readonly List<GlobalHandlerEntry> _noGlobalHandler = new List<GlobalHandlerEntry>();

    /// <summary>
    /// Template (no Owner): its own useGlobalHandler. Instance: always its portrait template's (portraitBaseIDOverride, else Owner's BaseID)
    /// </summary>
    [JsonIgnore] public List<GlobalHandlerEntry> GlobalHandlerEntries
    {
        get
        {
            if (Owner == null) return useGlobalHandler ?? _noGlobalHandler;
            var bases = scr_System_Serializer.current?.MasterList?.Character_Bases;
            if (bases == null) return _noGlobalHandler;
            Character_SerializableBase template = null;
            if (!string.IsNullOrEmpty(portraitBaseIDOverride)) template = bases.GetByID(portraitBaseIDOverride);
            if (template == null && Owner != null) template = bases.GetByID(Owner.BaseID);
            if (template == null || template.Portrait == null || template.Portrait.useGlobalHandler == null) return _noGlobalHandler;
            return template.Portrait.useGlobalHandler;
        }
    }

    /// <summary>
    /// Handlers generated from the global portrait template, matched before portraitPriorityList. Never saved:
    /// not part of serializedData / _portraitCache, so MarkCustomized and Prepend only ever see the original handlers.
    /// </summary>
    [JsonIgnore] public List<CharaPortrait> GeneratedHandlers
    {
        get
        {
            return scr_System_CentralControl.current.GetGlobalPortraitHandlers(PortraitTemplateID);
        }
    }

    [JsonIgnore] public string PortraitTemplateID => string.IsNullOrEmpty(portraitBaseIDOverride) ? Owner?.BaseID : portraitBaseIDOverride;

    /// <summary>Shared generated assets for this portrait template, independent of useGlobalHandler.</summary>
    [JsonIgnore] public List<CharaPortrait> GeneratedAssetHandlers
    {
        get
        {
            RefreshGeneratedAssetVersion();
            return scr_System_CentralControl.current.GetGlobalPortraitHandlers(PortraitTemplateID, true);
        }
    }
    protected int _generatedAssetVersion = 0;
    protected string _generatedAssetTemplateID = null;

    void RefreshGeneratedAssetVersion()
    {
        var templateID = PortraitTemplateID;
        var version = GlobalPortraitTemplate.Revision;
        if (_generatedAssetVersion == version && string.Equals(_generatedAssetTemplateID, templateID, StringComparison.OrdinalIgnoreCase)) return;
        InvalidateGeneratedAssetCache();
        _generatedAssetTemplateID = templateID;
        _generatedAssetVersion = version;
    }

    /// <summary>New assets invalidate both selection and its activity context; the next draw collects fresh paired tags.</summary>
    public void InvalidateGeneratedAssetCache()
    {
        ResetGeneratedAssetHandlers();
        // Message-driven tags can reference shared lists; replace rather than clearing them in place.
        tags_active = new List<string>();
        tags_active_target = new List<string>();
    }

    void ResetGeneratedAssetHandlers()
    {
        _generatedAssetVersion = 0;
        _generatedAssetTemplateID = null;
        _cache_NeutralPortrait = null;
        _cache_CombatPortrait = null;
        _cache_ActivityPortrait = null;
    }

    /// <summary>
    /// The original handler a draw without context keys picks: first enabled one with no Require*ContextKeys, else the last (GetValidPortrait's fallback)
    /// </summary>
    [JsonIgnore] public CharaPortrait NeutralHandler
    {
        get
        {
            var list = portraitPriorityList;
            if (list.Count < 1) return null;
            foreach (var i in list)
            {
                if (i == null || !i.isValid()) continue;
                if (i.RequireSelfContextKeys.Count > 0 || i.RequireTargetContextKeys.Count > 0) continue;
                return i;
            }
            return list[list.Count - 1];
        }
    }

    static string IconPathOf(CharaPortrait p)
    {
        switch (p)
        {
            case CharaPortrait_Image i: return i.icon_path;
            case CharaPortrait_Spine s: return s.icon_path;
            case CharaPortrait_LandscapeImage l: return l.icon_path;
            default: return "";
        }
    }

    /// <summary>
    /// Matching order: configured global handlers, generated-asset handlers, then original handlers
    /// </summary>
    [JsonIgnore] public IEnumerable<CharaPortrait> ActivePortraits
    {
        get
        {
            foreach (var i in GeneratedHandlers) yield return i;
            foreach (var i in GeneratedAssetHandlers) yield return i;
            foreach (var i in portraitPriorityList) yield return i;
        }
    }

    public PortraitManager()
    {
        //Debug.Log("PortraitManager instantiate on null call");
    }
    public PortraitManager(Character_Trainable c)
    {
        this._owner = c;
    }
    protected Character_Trainable _owner = null;
    [JsonIgnore] public Character_Trainable Owner { get { return _owner; } }


    /// <summary>
    /// Rebuilds serialized handlers. Cached (template) handlers are dropped and lazily rebuilt on next access.
    /// </summary>
    public void RebuildInternal(Character_Trainable c)
    {
        _owner = c;
        ResetGeneratedAssetHandlers();
        if (serializedData != null)
        {
            foreach (var i in serializedData)
            {
                i.RebuildInternal();
                i.Owner = this;
            }
        }
        else _portraitCache = null;

        if (serializedBanner != null) serializedBanner.Owner = this;
        else
        {
            _bannerCache = null;
            _bannerCacheInit = false;
        }
    }
    public IEnumerator CacheInternal(Character_Trainable c)
    {
        _owner = c;
        // Generated image handlers have no preload work. Do not enumerate/build them on save load.
        foreach (var i in portraitPriorityList)
        {
            i.Owner = this;
            var cache = i.CacheInternal();
            if (cache != null) yield return cache;
        }

        if (this.CharaBanner != null)
        {
            var cache = this.CharaBanner.CacheInternal();
            if (cache != null) yield return cache;
        }
    }

    public bool CanResetPortrait(out string tooltip)
    {
        var baseTemplate = scr_System_Serializer.current.MasterList.Character_Bases.GetByID(this.Owner.BaseID);
        if (baseTemplate == null)
        {
            tooltip = $"cannot find Base Character with baseID {Owner.BaseID}";
            return false;
        }
        else if (baseTemplate.Portrait == null || baseTemplate.Portrait.portraitPriorityList.Count < 1)
        {
            tooltip = $"Base Character {Owner.BaseID} does not have Portrait manager";
            return false;
        }
        else
        {
            tooltip = $"found character template {baseTemplate.baseID}";
            return true;
        }
    }

    /// <summary>
    /// Call when destory
    /// </summary>
    public void ClearInternal()
    {
        // backing fields only - do not populate the cache just to destroy it
        var list = serializedData ?? _portraitCache;
        if (list != null) foreach (var i in list) i.Destroy();
        ResetGeneratedAssetHandlers();
    }

    [JsonIgnore]
    public Character_SerializableBase portraitTemplate
    {
        get
        {
            Character_SerializableBase returnvalue = null;
            if (this.portraitBaseIDOverride != "")
            {
                returnvalue = scr_System_Serializer.current.MasterList.Character_Bases.GetByID(portraitBaseIDOverride);
                if (returnvalue != null) return returnvalue;
            }
            returnvalue = scr_System_Serializer.current.MasterList.Character_Bases.GetByID(this.Owner.BaseID);
            return returnvalue;
        }
    }

    public void SetTemplate(Character_SerializableBase template)
    {
        if (template == null) this.portraitBaseIDOverride = "";
        else
        {
            this.portraitBaseIDOverride = template.baseID;
        }
        ResetPortraits();
    }

    /// <summary>
    /// Full reset of everything internal
    /// </summary>
    public void ResetPortraits()
    {

        ClearInternal();
        ClearHandlerCache();
        var baseTemplate = portraitTemplate;

        // back to cache mode: handlers are resolved from the template on next access, nothing saved until customized
        InitFromTemplate(baseTemplate != null ? baseTemplate.baseID : "");

       // Debug.Log($"Portrait reset, preReset count {i} target template count {baseTemplate.Portrait.portraitPriorityList.Count} final count {this.portraitPriorityList.Count}");
        RebuildInternal(this.Owner);

    }

    IEnumerator DrawHandlerIcon(CharaPortrait handler, scr_CharIconBox box, string path)
    {
        yield return (handler.isGenerated ? NeutralHandler ?? handler : handler).DrawIcon(box, path);
    }

    public void Prepend(CharaPortrait cm)
    {
        MarkCustomized();
        this.portraitPriorityList.Insert(0,cm);
        cm.Owner = this;
        ResetGeneratedAssetHandlers();

        //Debug.Log("new portrait, owner ["+Owner.FirstName+"] cmOwner["+cm.Owner.Owner.FirstName+"] isImage[" + (cm is PortraitManager.CharaPortrait_Image) + "] listCount["+portraitPriorityList.Count+"] variantsCount [" + cm.Variants.Count + "] isvalid[" + (cm.isValid()) + "] portraitPath[" + (cm as PortraitManager.CharaPortrait_Image).PortraitPath + "]");
    }

    protected CharaPortrait_Image _transparent = null;
    protected CharaPortrait_Image Transparent
    {
        get
        {
            if (_transparent == null) _transparent = new CharaPortrait_Image("", "");
            return _transparent;    
        }
    }
    List<string> newstr = new List<string>();
    protected void GetValidPortrait(List<string> keywords, List<string> keywords_target, out CharaPortrait handler, out string portrait, out string icon, scr_CharPortraitBox box = null)
    {
        handler = null;
        portrait = "";
        icon = "";
        if (portraitPriorityList.Count < 1 && GeneratedHandlers.Count < 1 && GeneratedAssetHandlers.Count < 1)
        {
            handler = Transparent;
        }
        else
        {
            foreach (var i in ActivePortraits)
            {
                if (!i.isValid()) continue;
                if (!i.isValidForBox(box)) continue;
                if ((keywords == null || keywords.Count < 1) && i.RequireSelfContextKeys.Count > 0) continue;
                if ((keywords_target == null || keywords_target.Count < 1) && i.RequireTargetContextKeys.Count > 0) continue;
                if (i.charaReq != null)
                {
                    newstr.Clear();
                    if (Owner == null || Owner.RefID == -1 || Owner.Stats == null)
                    {
                        Debug.Log($"portrait validation fail [{Owner == null} {(Owner == null || Owner.RefID == -1)} {(Owner == null || Owner.RefID == -1 || Owner.Stats == null)}]");
                        continue;
                    }
                    else if (!CharaReqUtility.Validate(i.charaReq, ref newstr, Owner, out bool hardlock))
                    {
                        //Debug.Log($"portrait validation fail [{String.Join("|", newstr)}]");
                        continue;
                    }
                }
                if (!Utility.ListContainsStrict(keywords, i.RequireSelfContextKeys))
                {
                    //Debug.Log($"portrait prioriry missing [{String.Join(" ",i.RequireContextKeys)}] from [{String.Join(" ", keywords)}]");
                    continue;
                }
                if (!Utility.ListContainsStrict(keywords_target, i.RequireTargetContextKeys))
                {
                    //Debug.Log($"portrait prioriry missing [{String.Join(" ",i.RequireContextKeys)}] from [{String.Join(" ", keywords)}]");
                    continue;
                }
                portrait = i is CharaPortrait_Image image && i.isGenerated ? image.PortraitPath(keywords, box, Owner) : i.PortraitPath(keywords, box);
                icon = i.isGenerated ? IconPathOf(NeutralHandler) : i.IconPath(keywords);
                if (string.IsNullOrEmpty(icon)) icon = portrait;

                if (portrait == "" || icon == "") continue;

                handler = i;
                break;
            }
            if (handler == null && portraitPriorityList.Count > 0)
            {   // fallback: the last original handler (generated handlers are never the fallback)
                handler = portraitPriorityList[portraitPriorityList.Count - 1];
                portrait = handler.PortraitPath(keywords, box);
                icon = handler.IconPath(keywords) != "" ? handler.IconPath(keywords) : portrait;
            }
            else if (handler == null)
            {
                handler = Transparent;
                portrait = "";
                icon = "";
            }
        }
    }


    protected void GetValidPortraitWithHandler(List<string> keywords, List<string> target, in CharaPortrait handler, out string portrait, out string icon, scr_CharPortraitBox box = null)
    {
        if (handler == null || handler == Transparent || !handler.isValid() || !handler.isValidForBox(box) 
            || (keywords.Count < 1 && handler.RequireSelfContextKeys.Count > 0)
            || !Utility.ListContainsStrict(keywords, handler.RequireSelfContextKeys)
            || (target.Count < 1 && handler.RequireTargetContextKeys.Count > 0)
            || !Utility.ListContainsStrict(target, handler.RequireTargetContextKeys))
        {
            portrait = "";
            icon = "";
            return;
        }

        portrait = handler is CharaPortrait_Image image && handler.isGenerated ? image.PortraitPath(keywords, box, Owner) : handler.PortraitPath(keywords, box);
        icon = handler.isGenerated ? IconPathOf(NeutralHandler) : handler.IconPath(keywords);
        if (string.IsNullOrEmpty(icon)) icon = portrait;
    }

    public void ClearHandlerCache()
    {
        _cache_NeutralPortrait = null;
        _cache_CombatPortrait = null;
        _cache_ActivityPortrait = null;
        //tags_active.Clear();
        if (scr_System_CentralControl.current.LogPrefs.DLog_Portraits && Owner != null && Owner.CurrentRoom == scr_System_CampaignManager.current.CurrentRoom) Debug.Log($"ClearHandlerCache on {Owner.FirstName}");
    }


    [JsonIgnore] public CharaPortrait _cache_NeutralPortrait = null;
    protected string _cache_NeutralPortrait_path = "";
    protected string _cache_NeutralPortrait_icon = "";

    public void DrawBanner(scr_CharPortraitBox box)
    {
        if (this.CharaBanner != null)
        {
            box.Draw(this.CharaBanner.DrawPortrait(box, this.CharaBanner.PortraitPath(new List<string>(), box)));
        }
        else
        {

        }
    }
    protected void DrawNeutralPortrait(scr_CharPortraitBox box)
    {
        RefreshGeneratedAssetVersion();
        CharaPortrait handler;
        string path;

        if (_cache_ActivityPortrait != null)
        {
            handler = _cache_ActivityPortrait;
            path = _cache_ActivityPortrait_path;
        }
        else
        {
            if (_cache_NeutralPortrait == null)
            {
                tags_neutral = GetOwnerActionTagsByPriority();
                tags_neutral_target = GetOwnerActionTargetTagsByPriority();
                GetValidPortrait(tags_neutral, tags_neutral_target, out _cache_NeutralPortrait, out _cache_NeutralPortrait_path, out _cache_NeutralPortrait_icon);

            }
            handler = _cache_NeutralPortrait;
            path = _cache_NeutralPortrait_path;

            if (scr_System_CentralControl.current.LogPrefs.DLog_Portraits) Debug.Log($"{Owner?.CallName} DrawNeutralPortrait Tags [{String.Join(" ", tags_neutral)}] with path {_cache_NeutralPortrait_path}, is same? {box.currentHandler == _cache_NeutralPortrait}");
        }

        if (box.currentHandler != handler || box.currentPortrait != path)
        {
            box.currentHandler = handler;
           // box.currentPortrait = path;
            scr_System_CampaignManager.current.CurrentTargetEXPortrait = box.currentHandler;
            box.Draw(handler.DrawPortrait(box, path));
        }
        else if (path != "" && !(handler is CharaPortrait_Spine) && !box.IsShowingWebp(path) && !scr_System_CentralControl.current.GetSprite(path, out _))
        {
            // same portrait as before, but its cached sprite lapsed while still on screen - reload it
            // (spine handlers and playing animated webp don't route their main portrait through the sprite cache, so they're excluded)
            box.Draw(handler.DrawPortrait(box, path));
        }
    }

    public void CollectAllTags(List<string> s, List<string> s2)
    {
        foreach(var portrait in this.ActivePortraits)
        {
            s.AddRange(portrait.RequireSelfContextKeys);
            s2.AddRange(portrait.RequireTargetContextKeys);
            foreach(var variant in portrait.Variants)
            {
                s.AddRange(variant.tagsMatch);
                // s2.AddRange(variant.tagsMatch);
            }
        }
        Utility.DistinctInPlace(s);
        Utility.DistinctInPlace(s2);
    }


    [JsonIgnore] public CharaPortrait _cache_CombatPortrait = null;
    protected string _cache_CombatPortrait_path = "";
    protected string _cache_CombatPortrait_icon = "";
    public void DrawCombatPortrait(I_StatsManager stats, scr_CharPortraitBox box, bool forceRefresh = false)
    {
        RefreshGeneratedAssetVersion();
        if (_cache_CombatPortrait == null || forceRefresh)
        {
            GetValidPortrait(new List<string>() { "combat" }, null, out _cache_CombatPortrait, out _cache_CombatPortrait_path, out _cache_CombatPortrait_icon, box);
            tags_combat = new List<string>() { "combat" };
        }
        if (box.currentHandler != _cache_CombatPortrait || box.currentPortrait != _cache_CombatPortrait_path)
        {
            box.currentHandler = _cache_CombatPortrait;
            //box.currentPortrait = _cache_CombatPortrait_path;
            box.Draw( _cache_CombatPortrait.DrawPortrait(box, _cache_CombatPortrait_path));
        }
        else if (_cache_CombatPortrait_path != "" && !(_cache_CombatPortrait is CharaPortrait_Spine) && !box.IsShowingWebp(_cache_CombatPortrait_path) && !scr_System_CentralControl.current.GetSprite(_cache_CombatPortrait_path, out _))
        {
            // same portrait as before, but its cached sprite lapsed while still on screen - reload it
            // (spine handlers and playing animated webp don't route their main portrait through the sprite cache, so they're excluded)
            box.Draw(_cache_CombatPortrait.DrawPortrait(box, _cache_CombatPortrait_path));
        }
    }
    public void DrawCombatIcon(I_StatsManager stats, scr_CharIconBox box, bool forceRefresh = false)
    {
        RefreshGeneratedAssetVersion();
        if (_cache_CombatPortrait == null || forceRefresh)
        {
            GetValidPortrait(new List<string>() { "combat" }, null, out _cache_CombatPortrait, out _cache_CombatPortrait_path, out _cache_CombatPortrait_icon);
            tags_combat = new List<string>() { "combat" };
        }
        if (box.currentHandler != _cache_CombatPortrait || box.currentIcon != _cache_CombatPortrait_icon)
        {
            box.currentHandler = _cache_CombatPortrait;
            box.currentIcon = _cache_CombatPortrait_icon;
            box.Draw(DrawHandlerIcon(_cache_CombatPortrait, box, _cache_CombatPortrait_icon));
        }
        else if (_cache_CombatPortrait_icon != "" && !scr_System_CentralControl.current.GetSprite(_cache_CombatPortrait_icon, out _))
        {
            // same icon as before, but its cached sprite lapsed while still on screen - reload it
            box.Draw(DrawHandlerIcon(_cache_CombatPortrait, box, _cache_CombatPortrait_icon));
        }
    }
    protected CharaPortrait _cache_ActivityPortrait = null;
    protected string _cache_ActivityPortrait_path = "";
    protected string _cache_ActivityPortrait_icon = "";
    /// <summary>
    /// Draws the "current activity" portrait shown in the CurrentTarget box. Tag source depends on the call path:
    /// <br/>- handler != null (a dialogue/message line just fired, eg. from Log_TrySetChara): forces a refresh and adopts
    ///   that message's own SelfPortraitTag/TargetPortraitTag as the new tags_active/tags_active_target pair.
    /// <br/>- handler == null and forceRefresh (portrait click): collect fresh paired tags from the current activity.
    /// <br/>- handler == null, no forced refresh, and tags_active is already populated (left over from a previous handler-driven call, or a
    ///   previous fresh collect below): reuse tags_active/tags_active_target as-is. Does NOT re-collect and does NOT
    ///   wipe the paired target tags - they must survive together, since they describe the same moment.
    /// <br/>- handler == null and tags_active is empty (nothing cached yet, eg. first draw or cache was cleared):
    ///   collect fresh from Owner's currently active job/interaction - self tags via GetOwnerActionTagsByPriority,
    ///   partner tags via GetOwnerActionTargetTagsByPriority.
    /// <br/>Note: tags_active/tags_active_target and the _cache_ActivityPortrait* fields are shared with drawActivityIcon
    /// (same Owner, same "current activity"), so either code path can populate or invalidate them for the other.
    /// </summary>
    protected void DrawActivityPortrait(scr_CharPortraitBox box, I_hasPortrait handler = null, bool forceRefresh = false, bool lowPriority = false)
    {
        RefreshGeneratedAssetVersion();
        //Debug.Log($"{Owner.CallName} DrawActivityPortrait");
        forceRefresh = forceRefresh || handler != null;
        if (_cache_ActivityPortrait == null || forceRefresh)
        {
            if (handler != null)
            {
                tags_active = handler.SelfPortraitTag;
                tags_active_target = handler.TargetPortraitTag;
            }
            else if (forceRefresh || tags_active.Count < 1)
            {
                tags_active = GetOwnerActionTagsByPriority();
                tags_active_target = GetOwnerActionTargetTagsByPriority();
            }
            // else: reuse the already-cached tags_active/tags_active_target pair as-is

            GetValidPortrait(tags_active, tags_active_target, out _cache_ActivityPortrait, out _cache_ActivityPortrait_path, out _cache_ActivityPortrait_icon, box);
        }
        if (box.currentHandler != _cache_ActivityPortrait || box.currentPortrait != _cache_ActivityPortrait_path)
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Portraits) Debug.Log($"{Owner?.CallName} DrawActivityPortrait Tags [{String.Join(" ", tags_active)}] with path {_cache_ActivityPortrait_path}, is same? {box.currentHandler == _cache_ActivityPortrait}");
            box.currentHandler = _cache_ActivityPortrait;
            //box.currentPortrait = _cache_ActivityPortrait_path;
            box.Draw(_cache_ActivityPortrait.DrawPortrait(box, _cache_ActivityPortrait_path, lowPriority));
            return;
        }
        else if (box.currentHandler != null && tags_active != null) 
        {
            GetValidPortraitWithHandler(tags_active, tags_active_target, in _cache_ActivityPortrait, out var ppath, out var ipath, box);
            if (ppath != "" && ppath != _cache_ActivityPortrait_path)
            {
                _cache_ActivityPortrait_path = ppath;
                box.Draw(_cache_ActivityPortrait.DrawPortrait(box, _cache_ActivityPortrait_path, lowPriority));
            }
            else if (_cache_ActivityPortrait_path != "" && !(_cache_ActivityPortrait is CharaPortrait_Spine) && !box.IsShowingWebp(_cache_ActivityPortrait_path) && !scr_System_CentralControl.current.GetSprite(_cache_ActivityPortrait_path, out _))
            {
                // same portrait as before, but its cached sprite lapsed while still on screen - reload it
                // (spine handlers and playing animated webp don't route their main portrait through the sprite cache, so they're excluded)
                box.Draw(_cache_ActivityPortrait.DrawPortrait(box, _cache_ActivityPortrait_path, lowPriority));
            }
            else
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_Portraits) Debug.Log($"{Owner?.CallName} DrawActivityPortrait ABORT 2: selection unchanged, handler [{_cache_ActivityPortrait?.GetType().Name}], path [{_cache_ActivityPortrait_path}], self tags [{String.Join(" ", tags_active)}], target tags [{String.Join(" ", tags_active_target)}]");

            }
        }
        else
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Portraits) Debug.Log($"{Owner?.CallName} DrawActivityPortrait ABORT 1 due to cannot find distinct portrait for Tags [{String.Join(" ", tags_active)}]");
        }

    }

    public List<string> GetOwnerActionTagsByPriority()
    {
        var result = new List<string>();
        if (Owner == null) return result;

        UtilityEX.GetActorTag(ref result, Owner);
        var packages = new List<ActionPackage>();
        if (Owner.InteractionJob != null && Owner.InteractionJob.isActive)
        {
            Owner.InteractionJob.GetActorAPTags(Owner.RefID, result, packages);
            //if (result.Count > 0) return result;
        }
        if (Owner.CurrentJob != null)
        {
            var tag = Owner.CurrentJob.JobTypeTag(Owner);
            if (tag != null) result.AddRange(tag);
            Owner.CurrentJob.GetActorAPTags(Owner.RefID, result, packages);
        }
        // portrait role (global portrait role blocks require doer/receiver): receiver in any EP wins, solo acts have no role
        // + COM portrait-only pose keywords of the EPs Owner acts in
        bool isReceiver = false, isDoer = false;
        foreach (var ap in packages)
        {
            foreach (var ep in ap.ListEP)
            {
                if (ep.Doer != Owner && ep.Receiver != Owner) continue;
                string role = "";
                if (ep.Receiver == Owner && ep.Doer != Owner) { isReceiver = true; role = "receiver"; }
                else if (ep.Doer == Owner && ep.Receiver != null && ep.Receiver != Owner) { isDoer = true; role = "doer"; }
                if (ep.targetCOM != null) ep.targetCOM.AddPortraitTags(result, role);
            }
        }
        if (isReceiver) result.Add("receiver");
        else if (isDoer) result.Add("doer");
        UtilityEX.AddPortraitDisplayTags(result);
        result = result.Distinct().ToList();
        return result;
    }

    /// <summary>
    /// Same as GetOwnerActionTagsByPriority, but for Owner's current interaction partner(s) instead of Owner itself.
    /// Feeds the targetTags side of portrait matching for the active-job (non-dialogue) draw path.
    /// </summary>
    public List<string> GetOwnerActionTargetTagsByPriority()
    {
        var result = new List<string>();
        if (Owner == null) return result;
        var packages = new List<ActionPackage>();
        if (Owner.InteractionJob != null && Owner.InteractionJob.isActive)
        {
            Owner.InteractionJob.GetActorAPTargetTags(Owner.RefID, result, packages);
        }
        if (Owner.CurrentJob != null)
        {
            Owner.CurrentJob.GetActorAPTargetTags(Owner.RefID, result, packages);
        }
        // Category requirements describe the partner, not just the partner's action in an EP.
        var partners = new HashSet<Character_Trainable>();
        foreach (var ap in packages.Distinct())
        {
            if (ap?.ListEP == null) continue;
            foreach (var ep in ap.ListEP)
            {
                if (ep == null) continue;
                var partner = ep.Doer == Owner ? ep.Receiver : ep.Receiver == Owner ? ep.Doer : null;
                if (partner != null && partner != Owner && partners.Add(partner)) UtilityEX.GetActorTag(ref result, partner);
            }
        }
        result = result.Distinct().ToList();
        return result;
    }

    [JsonIgnore] public List<string> tags_neutral = new List<string>(), tags_neutral_target = new List<string>(), tags_active = new List<string>(), tags_active_target = new List<string>(), tags_combat = new List<string>(), tags_combat_target = new List<string>();
    /// <summary>
    /// Draws the character icon (world map / room list, no dialogue involvement - there is no "handler" input here).
    /// Unlike DrawActivityPortrait, this always collects fresh self/target tags from Owner's currently active job
    /// whenever _cache_ActivityPortrait is null - it never reuses a stale tags_active from a previous call, since an
    /// icon box has no message-driven tags to prefer over the live job state. Note _cache_ActivityPortrait and
    /// tags_active/tags_active_target are shared with DrawActivityPortrait: whichever draw call runs first after a
    /// cache invalidation repopulates these fields for both.
    /// </summary>
    protected void drawActivityIcon(scr_CharIconBox box)
    {
        RefreshGeneratedAssetVersion();
        //Debug.Log($"{Owner.CallName} drawActivityIcon");
        if (_cache_ActivityPortrait == null)
        {
            var tags = GetOwnerActionTagsByPriority();
            var targetTags = GetOwnerActionTargetTagsByPriority();
            GetValidPortrait(tags, targetTags, out _cache_ActivityPortrait, out _cache_ActivityPortrait_path, out _cache_ActivityPortrait_icon);
            tags_active = tags;
            tags_active_target = targetTags;
        }
        if (box.currentHandler != _cache_ActivityPortrait || box.currentIcon != _cache_ActivityPortrait_icon)
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Portraits) Debug.Log($"{Owner?.CallName} drawActivityIcon Tags [{String.Join(" ", tags_active)}] with path {_cache_ActivityPortrait_icon}");
            box.currentHandler = _cache_ActivityPortrait;
            box.currentIcon = _cache_ActivityPortrait_icon;
            box.Draw(DrawHandlerIcon(_cache_ActivityPortrait, box, _cache_ActivityPortrait_icon));
        }
        else if (_cache_ActivityPortrait_icon != "" && !scr_System_CentralControl.current.GetSprite(_cache_ActivityPortrait_icon, out _))
        {
            // same icon as before, but its cached sprite lapsed while still on screen - reload it
            box.Draw(DrawHandlerIcon(_cache_ActivityPortrait, box, _cache_ActivityPortrait_icon));
        }

    }
    public void ActivityClick(scr_CharPortraitBox box = null)
    {
        if (box != null && box.currentlyRunning == null)
        {
             Debug.Log("clicked!");
            DrawActivityPortrait(box, null, true, true);
           // _cache_ActivityPortrait.Click();
        }
    }

    public void DrawPortrait(scr_CharPortraitBox box, I_hasPortrait handler = null)
    {
        if (box.isCurrentTargetEXBox) DrawNeutralPortrait(box);
        else if (box.isCurrentTargetEXBox || box.isCurrentTargetBox) DrawActivityPortrait(box, handler);
        else
        {
            DrawNeutralPortrait(box);
        }
    }
    public void DrawIcon(scr_CharIconBox box)
    {
        drawActivityIcon(box);
    }

    public class PortraitVariant
    {
        public List<PortraitValidators> Conditions = new List<PortraitValidators>();
        public List<string> PortraitVariantData = new List<string>();
        public string IconVariantData = "";
        public bool Enable = true;
        public List<string> tagsMatch = new List<string>();
        public CharaReq charaReq = null;

        /// <summary>
        /// Image handlers only (Image / LandscapeImage): PortraitVariantData resolved into image paths.
        /// Each entry is resolved in order: an existing image address (kept as is), else a folder (every image under it),
        /// else a regex fully matched against image addresses. Image paths that do not exist are dropped.
        /// Variants are shared by reference between copied handlers: the result depends only on this variant's data,
        /// so it is identical for every sharer, and a rebuild swaps in a new list instead of editing the old one in place.
        /// Read only - do not modify the returned list.
        /// </summary>
        [JsonIgnore] public List<string> ImagePaths
        {
            get
            {
                if (_imagePaths == null) RebuildImagePaths();
                return _imagePaths;
            }
        }
        [JsonIgnore] protected List<string> _imagePaths = null;

        static readonly List<string> _emptyPaths = new List<string>();

        public void RebuildImagePaths()
        {
            if (PortraitVariantData == null || PortraitVariantData.Count == 0)
            {   // no entries: share one empty list, no per-variant allocation
                _imagePaths = _emptyPaths;
                _portraitOnlyImagePaths = _emptyPaths;
                return;
            }
            var result = new List<string>();
            if (PortraitVariantData != null)
            {
                foreach (var entry in PortraitVariantData)
                {
                    if (string.IsNullOrEmpty(entry)) continue;
                    var serializer = scr_System_Serializer.current;
                    // 1. exact image address - added only if the file exists
                    if (serializer.ImageFileExists(entry)) { result.Add(entry); continue; }
                    // 2. folder - every image under it
                    var folderImages = serializer.GetAllImageFilesInFolder(entry.TrimEnd('/'));
                    if (folderImages.Count > 0) { result.AddRange(folderImages); continue; }
                    // 3. regex over image addresses, e.g. "Aemeath\/happy[_0-9]*\.(png|jpeg|webp|jpg)"
                    var regexImages = serializer.GetAllImageFilesByRegex(entry);
                    if (regexImages.Count > 0) { result.AddRange(regexImages); continue; }
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Portraits) Debug.LogWarning($"PortraitVariant entry [{entry}] matched no image file, folder or regex");
                }
            }
            Utility.DistinctInPlace(result);
            _imagePaths = result;
            _portraitOnlyImagePaths = null;
        }

        /// <summary>
        /// ImagePaths resolved elsewhere (GlobalPortraitTemplate resolves its regexes against one character folder's keys only).
        /// Swapped in as a new list like RebuildImagePaths.
        /// </summary>
        public void SetImagePaths(List<string> paths)
        {
            _imagePaths = paths ?? _emptyPaths;
            _portraitOnlyImagePaths = null;
        }

        public void AddImagePath(string path)
        {
            if (_imagePaths == null || ReferenceEquals(_imagePaths, _emptyPaths)) _imagePaths = new List<string>();
            if (!_imagePaths.Contains(path)) _imagePaths.Add(path);
            if (_portraitOnlyImagePaths != null && !ReferenceEquals(_portraitOnlyImagePaths, _emptyPaths))
            {
                if (!PortraitImageInfo.IsLandscape(path) && !_portraitOnlyImagePaths.Contains(path)) _portraitOnlyImagePaths.Add(path);
            }
            else _portraitOnlyImagePaths = null;
        }

        /// <summary>
        /// ImagePaths without landscape images (by file header), for boxes that cannot show landscape.
        /// Built on first use and swapped in as a new list like ImagePaths. Read only.
        /// </summary>
        [JsonIgnore] public List<string> PortraitOnlyImagePaths
        {
            get
            {
                if (_portraitOnlyImagePaths == null) _portraitOnlyImagePaths = ImagePaths.Where(p => !PortraitImageInfo.IsLandscape(p)).ToList();
                return _portraitOnlyImagePaths;
            }
        }
        [JsonIgnore] protected List<string> _portraitOnlyImagePaths = null;

        public bool Validate(Character_Trainable c)
        {
            if (!Enable) return false;
            if (charaReq != null)
            {
                if (c == null) return false;
                List<string> tooltip = null;
                if (!CharaReqUtility.Validate(charaReq, ref tooltip, c, out bool hardlock)) return false;
            }
            if (Conditions == null || Conditions.Count < 1) return true;
            foreach(var i in Conditions) if (c == null || !i.Validate(c)) return false;

            return true;
        }
        public bool Validate(List<string> tags)
        {
            if (!Enable) return false;
            if (tagsMatch.Count < 1) return true;
            if (Utility.ListContainsStrict(tags, tagsMatch)) return true;
            return false;
        }
    }

    public class PortraitValidators
    {
        public string requireNaked = "";
        public bool requireUnconscious = false;
        public bool Validate(Character_Trainable c)
        {
            bool returnVal = true;
            returnVal = (requireNaked == "" || c.Body.GetRevealingScoreByTag(requireNaked, BodyEquipLayer.None) <= 0) && returnVal;
            //if (requireUnconscious) Debug.Log($"RequireUnconscious, result {(requireUnconscious)} {c.Stats.isConsciousnessUnconscious} {c.Stats.Consciousness.SeverityDisplayName}");
            returnVal = (!requireUnconscious || c.Stats.isConsciousnessUnconscious ) && returnVal;
            return returnVal;
        }
    }

    public class CharaPortrait
    {
        /// <summary>
        /// Destroy cached spine assets
        /// </summary>
        public virtual void Destroy()
        {

        }

        [JsonIgnore] public PortraitManager Owner;

        /// <summary>
        /// Built from the global portrait template (PortraitManager.GeneratedHandlers): never saved, offsets never overridden
        /// </summary>
        [JsonIgnore] public bool isGenerated = false;

        public float portrait_offset_x;
        public float portrait_offset_y;
        public float portrait_offset_size;

        public float icon_offset_x;
        public float icon_offset_y;
        public float icon_offset_size;

        public bool AllowXAxisFlip = false;
        public bool Disable = false;

        public List<PortraitVariant> Variants = new List<PortraitVariant>();

        public virtual void RebuildInternal()
        {

        }
        public virtual IEnumerator CacheInternal()
        {
            return null;
        }

        internal virtual string IconPath(List<string> tags)
        {
            return "";
        }

        internal virtual string PortraitPath(List<string> tags)
        {
            return "";
        }

        /// <summary>
        /// Box-aware path pick. box may be null (neutral / icon draws). Default ignores the box.
        /// </summary>
        internal virtual string PortraitPath(List<string> tags, scr_CharPortraitBox box)
        {
            return PortraitPath(tags);
        }

        public CharaReq charaReq = null;

        public List<string> RequireSelfContextKeys = new List<string>();
        public List<string> RequireTargetContextKeys = new List<string>();
        public virtual bool isValid() { return false; }
        public virtual bool isValidForBox(scr_CharPortraitBox box) { return true; }
        public virtual IEnumerator DrawIcon(scr_CharIconBox iconBox, string pathOverride)
        {
            iconBox.picture.sprite = SpriteAsset.transparent;
            yield break;
        }
        /// <summary>
        /// Base variant always draw transparent regardless of path
        /// </summary>
        /// <param name="portraitBox"></param>
        /// <param name="pathOverride"></param>
        /// <returns></returns>
        public virtual IEnumerator DrawPortrait(scr_CharPortraitBox portraitBox, string pathOverride, bool lowPriority = false)
        {
            Debug.Log($"null drawportrait [{pathOverride}]");
            portraitBox.HideWebp();
            portraitBox.picture.gameObject.SetActive(true);
            if (portraitBox.spineRect != null) portraitBox.spineRect.gameObject.SetActive(false);
            if (portraitBox.picture_landscape_group != null) portraitBox.picture_landscape_group.alpha = 0;
            //portraitBox.spineLoader.gameObject.SetActive(false);
            portraitBox.picture.sprite = SpriteAsset.transparent;
            portraitBox.currentPortrait = pathOverride;
            portraitBox.NotifyEndDraw();
            yield break;
        }

        public virtual void SetPortraitOffsets(float offsetX, float offsetY, float offsetSize)
        {
            //Debug.LogError("Base Portrait setoffset called");
        }

        public virtual void Click()
        {
            Debug.Log("Portrait Clicked on "+Owner?.Owner?.FirstName);
        }

        // More internal condition validation with shared portrait position data

        public virtual CharaPortrait Copy()
        {
            Debug.Log("Portrait Copy on " + Owner?.Owner?.FirstName);
            return null;
        }
    }

    /// <summary>
    /// CharaPortrait_Image draw target: portrait = picture rect (with offsets), landscape = picture_landscape rect (fixed, no offsets),
    /// auto = decided per image from its native size (landscape only when width > height).
    /// </summary>
    public enum ImageOrientation { auto, portrait, landscape }

    public class CharaPortrait_Image : CharaPortrait
    {   // do not allow multiple transparent image layering -> should use spine instead.
        public string portrait_path = "";
        public List<string> random_portrait_path = new List<string>();
        public string random_portrait_folder = "";
        public string icon_path="";
        [JsonConverter(typeof(StringEnumConverter))] public ImageOrientation image_orientation = ImageOrientation.auto;

        public override void RebuildInternal()
        {
            if (isGenerated) return;    // variants already resolved by GlobalPortraitTemplate, PortraitVariantData holds unresolved regexes
            if (this.random_portrait_folder != "")
            {
                this.random_portrait_path = scr_System_Serializer.current.GetAllImageFilesInFolder(this.random_portrait_folder);
            }
            if (this.Variants != null) foreach (var i in this.Variants) i.RebuildImagePaths();
        }
        public override CharaPortrait Copy()
        {
            var newEntry = new CharaPortrait_Image();
            newEntry.portrait_offset_size = this.portrait_offset_size;
            newEntry.portrait_offset_x  = this.portrait_offset_x;
            newEntry.portrait_offset_y  = this.portrait_offset_y;
            newEntry.portrait_path = this.portrait_path;
            newEntry.random_portrait_folder = this.random_portrait_folder;
            newEntry.random_portrait_path = this.random_portrait_path;
            newEntry.icon_offset_size = this.icon_offset_size;
            newEntry.icon_offset_x = this.icon_offset_x;
            newEntry.icon_offset_y = this.icon_offset_y;
            newEntry.AllowXAxisFlip = this.AllowXAxisFlip;
            newEntry.Disable = this.Disable;
            newEntry.icon_path = this.icon_path;
            newEntry.image_orientation = this.image_orientation;
            newEntry.Variants = this.Variants;
            newEntry.RequireSelfContextKeys = this.RequireSelfContextKeys;
            newEntry.RequireTargetContextKeys = this.RequireTargetContextKeys;
            newEntry.charaReq = this.charaReq;

            return newEntry;
        }
        public CharaPortrait_Image()
        {

        }
        public CharaPortrait_Image(string portrait_path, string icon_path)
        {
            this.portrait_path = portrait_path;
            this.icon_path = icon_path;

            portrait_offset_x = 0;
            portrait_offset_y = 0;
            portrait_offset_size = 1;

            icon_offset_x = 0;
            icon_offset_y = 0;
            icon_offset_size = 1;
        }

        public override bool isValid() { return !Disable; }

        /// <summary>
        /// Forced landscape needs a landscape slot (same as CharaPortrait_LandscapeImage).
        /// Auto can't know the orientation before the image is picked, so it stays valid and falls back to portrait at draw time.
        /// </summary>
        public override bool isValidForBox(scr_CharPortraitBox box)
        {
            if (image_orientation == ImageOrientation.landscape) return box != null && box.picture_landscape_group != null;
            return true;
        }
        public override IEnumerator DrawIcon(scr_CharIconBox iconBox, string pathOverride)
        {

            if (pathOverride == "") yield return base.DrawIcon(iconBox, pathOverride);  // no fallback, draw transparent
            else
            {
                if (scr_System_CentralControl.current.GetSprite(pathOverride, out var sprite))
                {
                    iconBox.picture.sprite = sprite;
                }
                else
                {
                    Texture2D loaded = null;
                    yield return AssetsLoader.LoadTextureCoroutine(pathOverride, texture => loaded = texture);
                    iconBox.picture.sprite = scr_System_CentralControl.current.MakeSprite(pathOverride, loaded);
                }

                iconBox.picture.SetNativeSize();
                iconBox.picture.rectTransform.anchoredPosition = new Vector2(icon_offset_x, icon_offset_y);
                iconBox.picture.rectTransform.localScale = new Vector3(icon_offset_size, icon_offset_size, icon_offset_size);
            }
        }

        internal override string IconPath(List<string> tags)
        {
            if (this.Variants != null)
            {
                foreach (var i in this.Variants) if (i.IconVariantData != "" && i.IconVariantData.Length > 0 && i.Validate(tags) && i.Validate(Owner == null ? null : Owner.Owner) ) return i.IconVariantData;
            }
            return icon_path;
        }

        internal override string PortraitPath(List<string> tags)
        {
            if (this.Variants != null)
            {
                if (tags.Count > 0 && scr_System_CentralControl.current.LogPrefs.DLog_Portraits) Debug.Log($"Validating variants with tags {String.Join("|", tags)}");
                // image count first: variants with no images are skipped before the (costlier) tag/chara validation
                foreach (var i in this.Variants) if (i.ImagePaths.Count > 0 && i.Validate(tags) && i.Validate(Owner == null ? null : Owner.Owner)) return Utility.GetRandomElement(i.ImagePaths);
            }
            if (this.random_portrait_path.Count > 0) return Utility.GetRandomElement(this.random_portrait_path);
            else return portrait_path;
        }

        /// <summary>
        /// Auto orientation on a box without a landscape slot (or no box): pick from portrait-only images,
        /// same variant -> random folder -> portrait_path order. portrait_path is an explicit pick and is returned as is.
        /// </summary>
        internal override string PortraitPath(List<string> tags, scr_CharPortraitBox box)
        {
            return PortraitPath(tags, box, Owner?.Owner);
        }

        internal string PortraitPath(List<string> tags, scr_CharPortraitBox box, Character_Trainable character)
        {
            bool portraitOnly = image_orientation == ImageOrientation.auto && !BoxAcceptsLandscape(box);

            if (this.Variants != null)
            {
                foreach (var i in this.Variants)
                {
                    if (i.ImagePaths.Count == 0 || !i.Validate(tags) || !i.Validate(character)) continue;
                    var paths = portraitOnly ? i.PortraitOnlyImagePaths : i.ImagePaths;
                    if (paths.Count > 0) return Utility.GetRandomElement(paths);
                }
            }
            var portraitOnlyRandom = portraitOnly ? PortraitOnlyRandomPaths : random_portrait_path;
            if (portraitOnlyRandom.Count > 0) return Utility.GetRandomElement(portraitOnlyRandom);
            else return portrait_path;
        }

        /// <summary>
        /// random_portrait_path without landscape images, rebuilt whenever random_portrait_path is replaced. Read only.
        /// </summary>
        [JsonIgnore] protected List<string> PortraitOnlyRandomPaths
        {
            get
            {
                if (_portraitOnlyRandomPaths == null || _portraitOnlyRandomSource != random_portrait_path)
                {
                    _portraitOnlyRandomSource = random_portrait_path;
                    _portraitOnlyRandomPaths = random_portrait_path == null ? new List<string>() : random_portrait_path.Where(p => !PortraitImageInfo.IsLandscape(p)).ToList();
                }
                return _portraitOnlyRandomPaths;
            }
        }
        [JsonIgnore] protected List<string> _portraitOnlyRandomPaths = null;
        [JsonIgnore] protected List<string> _portraitOnlyRandomSource = null;

        public static bool BoxAcceptsLandscape(scr_CharPortraitBox box)
        {
            return box != null && box.picture_landscape_group != null && box.picture_landscape != null;
        }

        public override IEnumerator DrawPortrait(scr_CharPortraitBox portraitBox, string pathOverride, bool lowPriority = false)
        {
            if ( pathOverride == "")
            {
                yield return base.DrawPortrait(portraitBox, pathOverride);
            }
            else
            {
               // Debug.Log($"image drawportrait [{pathOverride}]");

                // animated webp: played in the portrait or landscape webp slot (orientation from the file header);
                // no player for that slot, or a failed load, falls through to a still draw of its first frame
                if (PortraitImageInfo.IsAnimatedWebp(pathOverride))
                {
                    bool landscape = UseLandscape(portraitBox, PortraitImageInfo.IsLandscape(pathOverride));
                    if (portraitBox.CanPlayAnimatedWebp(pathOverride, landscape))
                    {
                        yield return portraitBox.DrawAnimatedWebp(pathOverride, landscape);
                        if (portraitBox.IsShowingWebp(pathOverride))
                        {
                            portraitBox.currentPortrait = pathOverride;
                            if (!landscape) portraitBox.UpdateAnchor(this);  // landscape is fixed
                            portraitBox.NotifyEndDraw();
                            yield break;
                        }
                    }
                }

                if (!scr_System_CentralControl.current.GetSprite(pathOverride, out var sprite))
                {
                    Texture2D loaded = null;
                    yield return AssetsLoader.LoadTextureCoroutine(pathOverride, texture => loaded = texture);
                    sprite = scr_System_CentralControl.current.MakeSprite(pathOverride, loaded);
                }

                if (UseLandscape(portraitBox, sprite))
                {
                    // landscape images are fixed: no UpdateAnchor, offsets are not applied
                    portraitBox.HideWebp();
                    portraitBox.picture_landscape.sprite = sprite;
                    if (portraitBox.spineRect != null) portraitBox.spineRect.gameObject.SetActive(false);
                    portraitBox.picture.gameObject.SetActive(false);
                    portraitBox.picture_landscape_group.alpha = 1;
                    portraitBox.currentPortrait = pathOverride;
                }
                else
                {
                    portraitBox.HideWebp();
                    portraitBox.picture.sprite = sprite;
                    portraitBox.picture.gameObject.SetActive(true);
                    if (portraitBox.picture_landscape_group != null) portraitBox.picture_landscape_group.alpha = 0;
                    if (portraitBox.spineRect != null) portraitBox.spineRect.gameObject.SetActive(false);

                    portraitBox.currentPortrait = pathOverride;

                    portraitBox.UpdateAnchor(this);
                }
                portraitBox.NotifyEndDraw();
                //portraitBox.UpdateAnchor(this, portrait_offset_x, portrait_offset_y, portrait_offset_size);
            }
        }

        /// <summary>
        /// Landscape when forced, or in auto when the image is wider than tall (square counts as portrait).
        /// Boxes without a landscape slot always draw as portrait.
        /// </summary>
        protected bool UseLandscape(scr_CharPortraitBox portraitBox, Sprite sprite)
        {
            return UseLandscape(portraitBox, sprite != null && sprite.rect.width > sprite.rect.height);
        }

        /// <summary>Same rule, with the auto decision given directly (eg. from the file header for animated webp).</summary>
        protected bool UseLandscape(scr_CharPortraitBox portraitBox, bool isWide)
        {
            if (!BoxAcceptsLandscape(portraitBox)) return false;
            switch (image_orientation)
            {
                case ImageOrientation.landscape: return true;
                case ImageOrientation.portrait: return false;
                default: return isWide;
            }
        }

        public override void SetPortraitOffsets(float offsetX, float offsetY, float offsetSize)
        {
            if (image_orientation == ImageOrientation.landscape) return;    // landscape images are fixed
            if (isGenerated) return;    // generated handlers are never overridden (and never saved)
            Owner?.MarkCustomized();
            this.portrait_offset_x = offsetX;
            this.portrait_offset_y = offsetY;
            this.portrait_offset_size = offsetSize;
        }
    }

    public class CharaPortrait_Spine : CharaPortrait
    {   // do not allow multiple transparent image layering -> should use spine instead.
        public List<string> materialTexturePaths = new List<string>();
        public string atlasJSON_path = "";
        public string skeletonJSON_path = "";
        //public float skeletonScale;
        public string idleAnimName = "";
        public string addonAnimName = "";

        public string icon_path = "";
        public bool straightAlpha = false;
        public CharaPortrait_Spine()
        {

        }
        public override CharaPortrait Copy()
        {
            var newEntry = new CharaPortrait_Spine();
            newEntry.portrait_offset_size = this.portrait_offset_size;
            newEntry.portrait_offset_x = this.portrait_offset_x;
            newEntry.portrait_offset_y = this.portrait_offset_y;

            newEntry.materialTexturePaths = this.materialTexturePaths;
            newEntry.atlasJSON_path = this.atlasJSON_path;
            newEntry.skeletonJSON_path = this.skeletonJSON_path;
            newEntry.idleAnimName = this.idleAnimName;
            newEntry.addonAnimName = this.addonAnimName;
            newEntry.straightAlpha = this.straightAlpha;

            newEntry.icon_offset_size = this.icon_offset_size;
            newEntry.icon_offset_x = this.icon_offset_x;
            newEntry.icon_offset_y = this.icon_offset_y;
            newEntry.AllowXAxisFlip = this.AllowXAxisFlip;
            newEntry.Disable = this.Disable;
            newEntry.icon_path = this.icon_path;
            newEntry.Variants = this.Variants;
            newEntry.RequireSelfContextKeys = this.RequireSelfContextKeys;
            newEntry.RequireTargetContextKeys = this.RequireTargetContextKeys;
            newEntry.charaReq = this.charaReq;

            return newEntry;
        }
        /*
        public CharaPortrait_Spine(string icon_path, List<string> materialTexturePath, string atlasJSON_path, string skeletonJSON_path, float skeletonScale, string idleAnimName)
        {
            this.materialTexturePaths = materialTexturePath;
            this.atlasJSON_path = atlasJSON_path;
            this.skeletonJSON_path = skeletonJSON_path;
            //this.skeletonScale = skeletonScale;
            this.idleAnimName = idleAnimName;


            portrait_offset_x = 0;
            portrait_offset_y = 0;
            portrait_offset_size = 1;

            icon_offset_x = 0;
            icon_offset_y = 0;
            icon_offset_size = 1;
        }*/

        public override void Destroy()
        {
            dataHolder = null; // owned by CentralControl spine cache; do not clear here
        }

        public override void RebuildInternal()
        {
            if (skeletonJSON_path == "" || atlasJSON_path == "") return;
            if (scr_System_CentralControl.current == null) return;
        }

        public override IEnumerator CacheInternal()
        {
            return PreCacheCoroutine();
        }

        private IEnumerator PreCacheCoroutine()
        {
            if (scr_System_CentralControl.current != null &&
                scr_System_CentralControl.current.TryGetSpineCache(skeletonJSON_path, out var cached))
            {
                dataHolder = cached;
                yield break;
            }

            string version = "";
            if (dataHolder?.skeletonTA != null)
            {
                var text = Encoding.UTF8.GetString(dataHolder.skeletonTA,0,100);
                if (text.Contains("4.0.")) version = "4.0";
                else if (text.Contains("4.1.")) version = "4.1";
                else if (text.Contains("4.2.")) version = "4.2";
            }
            else
            {
                byte[] ta = null;
                yield return AssetsLoader.LoadSkelCoroutine(skeletonJSON_path, text => ta = text);
                if (ta == null) yield break;
                var text = Encoding.UTF8.GetString(ta, 0, 100);
                if (text.Contains("4.0."))
                {
                    this.dataHolder = new SpineDataTiny_40();
                    version = "4.0";
                }
                else if (text.Contains("4.1."))
                {
                    this.dataHolder = new SpineDataTiny_41();
                    version = "4.1";
                }
                else if (text.Contains("4.2."))
                {
                    this.dataHolder = new SpineDataTiny_42();
                    version = "4.2";
                }
                else
                {
                    yield break;
                }
                this.dataHolder.skeletonTA = ta;
                this.dataHolder.skeletonPath = skeletonJSON_path;

            }

            var animator = scr_System_CentralControl.current?.GetSpineAnimator(version);
            if (animator == null) yield break;
            yield return animator.PreCacheData(this, materialTexturePaths, atlasJSON_path, skeletonJSON_path, straightAlpha);

            if (dataHolder != null && dataHolder.initialized)
                scr_System_CentralControl.current?.RegisterSpineCache(skeletonJSON_path, dataHolder);
        }

        public override bool isValid() { return !Disable; }
        public override IEnumerator DrawIcon(scr_CharIconBox iconBox, string pathOverride )
        {
            if (pathOverride == "") yield return base.DrawIcon(iconBox, pathOverride);
            else
            {
                if (scr_System_CentralControl.current.GetSprite(pathOverride, out var sprite))
                {
                    iconBox.picture.sprite = sprite;
                }
                else
                {
                    Texture2D loaded = null;
                    yield return AssetsLoader.LoadTextureCoroutine(pathOverride, texture => loaded = texture);
                    iconBox.picture.sprite = scr_System_CentralControl.current.MakeSprite(pathOverride, loaded);
                }

                iconBox.picture.SetNativeSize();
                iconBox.picture.rectTransform.anchoredPosition = new Vector2(icon_offset_x, icon_offset_y);
                iconBox.picture.rectTransform.localScale = new Vector3(icon_offset_size, icon_offset_size, icon_offset_size);
            }
        }

        [JsonIgnore] public SpineDataTiny dataHolder = null;

        internal override string IconPath(List<string> tags)
        {
            if (this.Variants != null)
            {
                foreach (var i in this.Variants) if (i.IconVariantData != "" && i.IconVariantData.Length > 0 && i.Validate(tags) && i.Validate(Owner == null ? null : Owner.Owner)) return i.IconVariantData;
            }
            return icon_path;
        }

        internal override string PortraitPath(List<string> tags)
        {
            if (this.Variants != null)
            {
                foreach (var i in this.Variants) if (i.Validate(tags) && i.Validate(Owner == null ? null : Owner.Owner) && i.PortraitVariantData.Count > 0) return Utility.GetRandomElement(i.PortraitVariantData);
            }
            return this.addonAnimName;
        }

        /// <summary>
        /// Spine variant
        /// </summary>
        /// <param name="portraitBox"></param>
        /// <param name="pathOverride">pathOverride is its animation variant name</param>
        /// <returns></returns>
        public override IEnumerator DrawPortrait(scr_CharPortraitBox portraitBox, string pathOverride, bool lowPriority = false)
        {
            if (idleAnimName == "" && pathOverride == "")
            {
                //portraitBox.spineLoader.gameObject.SetActive(false);
                yield return base.DrawPortrait(portraitBox, pathOverride, lowPriority);

            }
            else
            {
                if (idleAnimName == "")
                {
                    yield return portraitBox.spineLoader.SetBase(this, materialTexturePaths, atlasJSON_path, skeletonJSON_path, straightAlpha, pathOverride, "", lowPriority);
                }
                else
                {
                    yield return portraitBox.spineLoader.SetBase(this, materialTexturePaths, atlasJSON_path, skeletonJSON_path, straightAlpha, idleAnimName, pathOverride, lowPriority);
                }
                // Debug.Log($"spine drawportrait [{pathOverride}]");
                //Debug.LogError($"PortraitAnimation name {PortraitAnimName}");
                if (portraitBox == null || portraitBox.spineRect == null || !portraitBox.spineRect.gameObject.activeInHierarchy) yield break;
                portraitBox.HideWebp();
                portraitBox.spineRect.gameObject.SetActive(true);
                if (portraitBox.picture_landscape_group != null) portraitBox.picture_landscape_group.alpha = 0;
                portraitBox.picture.gameObject.SetActive(false);
                portraitBox.currentPortrait = pathOverride;
                portraitBox.spineRect.SetParent(portraitBox.spineLoader.transform, false);
                portraitBox.UpdateAnchor(this);
                portraitBox.NotifyEndDraw();
            }
        }

        public override void SetPortraitOffsets(float offsetX, float offsetY, float offsetSize)
        {
            //Debug.Log("SetPortraitOffsets [" + offsetX + "] ["+offsetY+"] ["+offsetSize+"]");
            Owner?.MarkCustomized();
            this.portrait_offset_x = offsetX;
            this.portrait_offset_y = offsetY;
            this.portrait_offset_size = offsetSize;
        }
    }

    public class CharaPortrait_LandscapeImage : CharaPortrait
    {
        public string portrait_path = "";
        public List<string> random_portrait_path = new List<string>();
        public string random_portrait_folder = "";
        public string icon_path = "";

        public override void RebuildInternal()
        {
            if (this.random_portrait_folder != "")
            {
                this.random_portrait_path = scr_System_Serializer.current.GetAllImageFilesInFolder(this.random_portrait_folder);
            }
            if (this.Variants != null) foreach (var i in this.Variants) i.RebuildImagePaths();
        }

        public override CharaPortrait Copy()
        {
            var newEntry = new CharaPortrait_LandscapeImage();
            newEntry.portrait_path = this.portrait_path;
            newEntry.random_portrait_folder = this.random_portrait_folder;
            newEntry.random_portrait_path = this.random_portrait_path;
            newEntry.icon_offset_size = this.icon_offset_size;
            newEntry.icon_offset_x = this.icon_offset_x;
            newEntry.icon_offset_y = this.icon_offset_y;
            newEntry.AllowXAxisFlip = this.AllowXAxisFlip;
            newEntry.Disable = this.Disable;
            newEntry.Variants = this.Variants;
            newEntry.RequireSelfContextKeys = this.RequireSelfContextKeys;
            newEntry.RequireTargetContextKeys = this.RequireTargetContextKeys;
            newEntry.icon_path = this.icon_path;
            newEntry.charaReq = this.charaReq;
            return newEntry;
        }

        public override bool isValid() { return !Disable; }

        public override bool isValidForBox(scr_CharPortraitBox box)
        {
            return box != null && box.picture_landscape_group != null;
        }

        internal override string PortraitPath(List<string> tags)
        {
            if (this.Variants != null)
            {
                foreach (var i in this.Variants) if (i.ImagePaths.Count > 0 && i.Validate(tags) && i.Validate(Owner == null ? null : Owner.Owner)) return Utility.GetRandomElement(i.ImagePaths);
            }
            if (this.random_portrait_path.Count > 0) return Utility.GetRandomElement(this.random_portrait_path);
            else return portrait_path;
        }

        internal override string IconPath(List<string> tags)
        {
            return icon_path;
        }
        public override IEnumerator DrawIcon(scr_CharIconBox iconBox, string pathOverride)
        {

            if (pathOverride == "") yield return base.DrawIcon(iconBox, pathOverride);  // no fallback, draw transparent
            else
            {
                if (scr_System_CentralControl.current.GetSprite(pathOverride, out var sprite))
                {
                    iconBox.picture.sprite = sprite;
                }
                else
                {
                    Texture2D loaded = null;
                    yield return AssetsLoader.LoadTextureCoroutine(pathOverride, texture => loaded = texture);
                    iconBox.picture.sprite = scr_System_CentralControl.current.MakeSprite(pathOverride, loaded);
                }

                iconBox.picture.SetNativeSize();
                iconBox.picture.rectTransform.anchoredPosition = new Vector2(icon_offset_x, icon_offset_y);
                iconBox.picture.rectTransform.localScale = new Vector3(icon_offset_size, icon_offset_size, icon_offset_size);
            }
        }

        public override IEnumerator DrawPortrait(scr_CharPortraitBox portraitBox, string pathOverride, bool lowPriority = false)
        {
            if (pathOverride == "")
            {
                yield return base.DrawPortrait(portraitBox, pathOverride);
            }
            else
            {
                // animated webp in the landscape webp slot; no player or a failed load falls through to a still draw of its first frame
                if (portraitBox.CanPlayAnimatedWebp(pathOverride, true))
                {
                    yield return portraitBox.DrawAnimatedWebp(pathOverride, true);
                    if (portraitBox.IsShowingWebp(pathOverride))
                    {
                        portraitBox.currentPortrait = pathOverride;
                        portraitBox.NotifyEndDraw();
                        yield break;
                    }
                }

                if (scr_System_CentralControl.current.GetSprite(pathOverride, out var sprite))
                {
                    portraitBox.picture_landscape.sprite = sprite;
                }
                else
                {
                    Texture2D loaded = null;
                    yield return AssetsLoader.LoadTextureCoroutine(pathOverride, texture => loaded = texture);
                    portraitBox.picture_landscape.sprite = scr_System_CentralControl.current.MakeSprite(pathOverride, loaded);
                }

                //portraitBox.picture_landscape.SetNativeSize();
                portraitBox.HideWebp();
                if (portraitBox.spineRect != null) portraitBox.spineRect.gameObject.SetActive(false);
                portraitBox.picture.gameObject.SetActive(false);
                if (portraitBox.picture_landscape_group != null) portraitBox.picture_landscape_group.alpha = 1;
                portraitBox.currentPortrait = pathOverride;
                portraitBox.NotifyEndDraw();
            }
        }
    }
}

