using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;


public class PortraitManager
{
    public List<CharaPortrait> portraitPriorityList = new List<CharaPortrait>();

    public CharaPortrait CharaBanner = null;

    public string portraitBaseIDOverride = "";

    public PortraitManager()
    {
        //Debug.Log("PortraitManager instantiate on null call");
    }
    public PortraitManager(Character_Trainable c)
    {
        this._owner = c;
        if (portraitPriorityList != null) foreach(var i in portraitPriorityList) i.Owner = this;
    }
    protected Character_Trainable _owner = null;
    [JsonIgnore] public Character_Trainable Owner { get { return _owner; } }


    public void RebuildInternal(Character_Trainable c)
    {
        _owner = c;
        if (portraitPriorityList != null) foreach (var i in portraitPriorityList) i.Owner = this;
        foreach(var i in this.portraitPriorityList)
        {
            i.RebuildInternal();
        }
    }
    public IEnumerator CacheInternal(Character_Trainable c)
    {
        _owner = c;
        if (portraitPriorityList != null) foreach (var i in portraitPriorityList) i.Owner = this;

        foreach (var i in this.portraitPriorityList)
        {
            yield return i.CacheInternal();
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
        foreach(var i in portraitPriorityList)
        {
            i.Destroy();
        }
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
        this.portraitPriorityList.Clear();

        if (baseTemplate != null)
        {
            foreach (var pp in baseTemplate.Portrait.portraitPriorityList)
            {
                this.portraitPriorityList.Add(pp.Copy());
            }
        }

       // Debug.Log($"Portrait reset, preReset count {i} target template count {baseTemplate.Portrait.portraitPriorityList.Count} final count {this.portraitPriorityList.Count}");
        RebuildInternal(this.Owner);

    }

    public void Prepend(CharaPortrait cm)
    {
        this.portraitPriorityList.Insert(0,cm);
        cm.Owner = this;

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
    protected void GetValidPortrait(List<string> keywords, out CharaPortrait handler, out string portrait, out string icon, scr_CharPortraitBox box = null)
    {
        handler = null;
        portrait = "";
        icon = "";
        if (portraitPriorityList.Count < 1)
        {
            handler = Transparent;
        }
        else
        {
            foreach (var i in portraitPriorityList)
            {
                if (!i.isValid()) continue;
                if (!i.isValidForBox(box)) continue;
                if (keywords.Count < 1 && i.RequireContextKeys.Count > 0) continue;
                if (!Utility.ListContainsStrict(keywords, i.RequireContextKeys))
                {
                    //Debug.Log($"portrait prioriry missing [{String.Join(" ",i.RequireContextKeys)}] from [{String.Join(" ", keywords)}]");
                    continue;
                }
                portrait = i.PortraitPath(keywords);
                icon = i.IconPath(keywords) != "" ? i.IconPath(keywords) : portrait;

                if (portrait == "" || icon == "") continue;

                handler = i;
                break;
            }
            if (handler == null)
            {
                handler = portraitPriorityList[portraitPriorityList.Count - 1];
                portrait = handler.PortraitPath(keywords);
                icon = handler.IconPath(keywords) != "" ? handler.IconPath(keywords) : portrait;
            }
        }
    }

    public void ClearHandlerCache()
    {
        _cache_NeutralPortrait = null;
        _cache_CombatPortrait = null;
        _cache_ActivityPortrait = null;
        //tags_active.Clear();
        if (scr_System_CentralControl.current.LogPrefs.DLog_Portraits && Owner.CurrentRoom == scr_System_CampaignManager.current.CurrentRoom) Debug.Log($"ClearHandlerCache on {Owner.FirstName}");
    }


    [JsonIgnore] public CharaPortrait _cache_NeutralPortrait = null;
    protected string _cache_NeutralPortrait_path = "";
    protected string _cache_NeutralPortrait_icon = "";

    public void DrawBanner(scr_CharPortraitBox box)
    {
        if (this.CharaBanner != null) box.Draw(this.CharaBanner.DrawPortrait(box, this.CharaBanner.PortraitPath(new List<string>())));
        else DrawNeutralPortrait(box);
    }
    protected void DrawNeutralPortrait(scr_CharPortraitBox box)
    {
        if (_cache_NeutralPortrait == null)
        {
            GetValidPortrait(new List<string>(), out _cache_NeutralPortrait, out _cache_NeutralPortrait_path, out _cache_NeutralPortrait_icon);
            tags_neutral.Clear();
        }
        if (box.currentHandler != _cache_NeutralPortrait || box.currentPortrait != _cache_NeutralPortrait_path)
        {
            box.currentHandler = _cache_NeutralPortrait;
           // box.currentPortrait = _cache_NeutralPortrait_path;
            scr_System_CampaignManager.current.CurrentTargetEXPortrait = box.currentHandler;
            box.Draw(_cache_NeutralPortrait.DrawPortrait(box, _cache_NeutralPortrait_path));
        }
    }

    public void CollectAllTags(ref List<string> s)
    {
        foreach(var portrait in this.portraitPriorityList)
        {
            s.AddRange(portrait.RequireContextKeys);
            foreach(var variant in portrait.Variants)
            {
                s.AddRange(variant.tagsMatch);
            }
        }
        s = Utility.Distinct(s);
    }


    [JsonIgnore] public CharaPortrait _cache_CombatPortrait = null;
    protected string _cache_CombatPortrait_path = "";
    protected string _cache_CombatPortrait_icon = "";
    public void DrawCombatPortrait(I_StatsManager stats, scr_CharPortraitBox box, bool forceRefresh = false)
    {
        if (_cache_CombatPortrait == null || forceRefresh)
        {
            GetValidPortrait(new List<string>() { "combat" }, out _cache_CombatPortrait, out _cache_CombatPortrait_path, out _cache_CombatPortrait_icon, box);
            tags_combat = new List<string>() { "combat" };
        }
        if (box.currentHandler != _cache_CombatPortrait || box.currentPortrait != _cache_CombatPortrait_path)
        {
            box.currentHandler = _cache_CombatPortrait;
            //box.currentPortrait = _cache_CombatPortrait_path;
            box.Draw( _cache_CombatPortrait.DrawPortrait(box, _cache_CombatPortrait_path));
        }
    }
    public void DrawCombatIcon(I_StatsManager stats, scr_CharIconBox box, bool forceRefresh = false)
    {
        if (_cache_CombatPortrait == null || forceRefresh)
        {
            GetValidPortrait(new List<string>() { "combat" }, out _cache_CombatPortrait, out _cache_CombatPortrait_path, out _cache_CombatPortrait_icon);
            tags_combat = new List<string>() { "combat" };
        }
        if (box.currentHandler != _cache_CombatPortrait || box.currentIcon != _cache_CombatPortrait_icon)
        {
            box.currentHandler = _cache_CombatPortrait;
            box.currentIcon = _cache_CombatPortrait_icon;
            box.Draw(_cache_CombatPortrait.DrawIcon(box, _cache_CombatPortrait_icon));
        }
    }
    protected CharaPortrait _cache_ActivityPortrait = null;
    protected string _cache_ActivityPortrait_path = "";
    protected string _cache_ActivityPortrait_icon = "";
    protected void DrawActivityPortrait(scr_CharPortraitBox box, List<string> tags = null, bool forceRefresh = false, bool lowPriority = false)
    {
        //Debug.Log($"{Owner.CallName} DrawActivityPortrait");
        forceRefresh = tags != null;
        if (_cache_ActivityPortrait == null || forceRefresh)
        {
            if (tags == null) tags = tags_active.Count > 0 ? tags_active : GetOwnerActionTagsByPriority();
            GetValidPortrait(tags, out _cache_ActivityPortrait, out _cache_ActivityPortrait_path, out _cache_ActivityPortrait_icon, box);
            tags_active = tags;
        }
        if (box.currentHandler != _cache_ActivityPortrait || box.currentPortrait != _cache_ActivityPortrait_path)
        {
           if (scr_System_CentralControl.current.LogPrefs.DLog_Portraits) Debug.Log($"{Owner.CallName} DrawActivityPortrait Tags [{String.Join(" ", tags_active)}] with path {_cache_ActivityPortrait_path}, is same? {box.currentHandler == _cache_ActivityPortrait}");
            box.currentHandler = _cache_ActivityPortrait;
            //box.currentPortrait = _cache_ActivityPortrait_path;
            box.Draw(_cache_ActivityPortrait.DrawPortrait(box, _cache_ActivityPortrait_path, lowPriority));
        }
        else
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Portraits) Debug.Log($"{Owner.CallName} DrawActivityPortrait ABORT due to cannot find distinct portrait for Tags [{String.Join(" ", tags_active)}]");
        }

    }

    public List<string> GetOwnerActionTagsByPriority()
    {
        var result = new List<string>();
        if (Owner.InteractionJob.isActive)
        {
            Owner.InteractionJob.GetActorAPTags(Owner.RefID, result);
            //if (result.Count > 0) return result;
        }
        if (Owner.CurrentJob != null)
        {
            var tag = Owner.CurrentJob.JobTypeTag(Owner);
            if (tag != null) result.AddRange(tag);
            Owner.CurrentJob.GetActorAPTags(Owner.RefID, result);
        }
        result = result.Distinct().ToList();
        return result;
    }

    [JsonIgnore] public List<string> tags_neutral = new List<string>(), tags_active = new List<string>(), tags_combat = new List<string>();
    protected void drawActivityIcon(scr_CharIconBox box)
    {
        //Debug.Log($"{Owner.CallName} drawActivityIcon");
        if (_cache_ActivityPortrait == null)
        {
            var tags = GetOwnerActionTagsByPriority();
            GetValidPortrait(tags, out _cache_ActivityPortrait, out _cache_ActivityPortrait_path, out _cache_ActivityPortrait_icon);
            tags_active = tags;
        }
        if (box.currentHandler != _cache_ActivityPortrait || box.currentIcon != _cache_ActivityPortrait_icon)
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Portraits) Debug.Log($"{Owner.CallName} drawActivityIcon Tags [{String.Join(" ", tags_active)}] with path {_cache_ActivityPortrait_icon}");
            box.currentHandler = _cache_ActivityPortrait;
            box.currentIcon = _cache_ActivityPortrait_icon;
            box.Draw(_cache_ActivityPortrait.DrawIcon(box, _cache_ActivityPortrait_icon));
        }

    }
    public void ActivityClick(scr_CharPortraitBox box = null)
    {
        if (box != null && box.currentlyRunning == null)
        {
            // Debug.Log("clicked!");
            DrawActivityPortrait(box, null, false, true);
           // _cache_ActivityPortrait.Click();
        }
    }

    public void DrawPortrait(scr_CharPortraitBox box, List<string> tags = null)
    {
        if (box.isCurrentTargetEXBox) DrawNeutralPortrait(box);
        else if (box.isCurrentTargetBox) DrawActivityPortrait(box, tags);
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
        public bool Validate(Character_Trainable c)
        {
            if (!Enable) return false;
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

        public List<string> RequireContextKeys = new List<string>();
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
            Debug.Log("Portrait Clicked on "+Owner.Owner.FirstName);
        }

        // More internal condition validation with shared portrait position data

        public virtual CharaPortrait Copy()
        {
            Debug.Log("Portrait Copy on " + Owner.Owner.FirstName);
            return null;
        }
    }

    public class CharaPortrait_Image : CharaPortrait
    {   // do not allow multiple transparent image layering -> should use spine instead.
        public string portrait_path = "";
        public List<string> random_portrait_path = new List<string>();
        public string random_portrait_folder = "";
        public string icon_path="";

        public override void RebuildInternal()
        {
            if (this.random_portrait_folder != "")
            {
                this.random_portrait_path = scr_System_Serializer.current.GetAllImageFilesInFolder(this.random_portrait_folder);
            }
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
            newEntry.Variants = this.Variants;
            newEntry.RequireContextKeys = this.RequireContextKeys;

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
                foreach (var i in this.Variants) if (i.IconVariantData != "" && i.IconVariantData.Length > 0 && i.Validate(tags) && i.Validate(Owner.Owner) ) return i.IconVariantData;
            }
            return icon_path;
        }

        internal override string PortraitPath(List<string> tags)
        {
            if (this.Variants != null)
            {
                if (tags.Count > 0 && scr_System_CentralControl.current.LogPrefs.DLog_Portraits) Debug.Log($"Validating variants with tags {String.Join("|", tags)}");
                foreach (var i in this.Variants) if (i.Validate(tags) && i.Validate(Owner.Owner) && i.PortraitVariantData.Count > 0) return Utility.GetRandomElement( i.PortraitVariantData);
            }
            if (this.random_portrait_path.Count > 0) return Utility.GetRandomElement(this.random_portrait_path);
            else return portrait_path;
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

                if (scr_System_CentralControl.current.GetSprite(pathOverride, out var sprite))
                {
                    portraitBox.picture.sprite = sprite;
                }
                else
                {
                    Texture2D loaded = null;
                    yield return AssetsLoader.LoadTextureCoroutine(pathOverride, texture => loaded = texture);
                    portraitBox.picture.sprite = scr_System_CentralControl.current.MakeSprite(pathOverride, loaded);
                }

                portraitBox.picture.gameObject.SetActive(true);
                if (portraitBox.picture_landscape_group != null) portraitBox.picture_landscape_group.alpha = 0;
                if (portraitBox.spineRect != null) portraitBox.spineRect.gameObject.SetActive(false);

                portraitBox.currentPortrait = pathOverride;

                portraitBox.UpdateAnchor(this);
                portraitBox.NotifyEndDraw();
                //portraitBox.UpdateAnchor(this, portrait_offset_x, portrait_offset_y, portrait_offset_size);
            }
        }

        public override void SetPortraitOffsets(float offsetX, float offsetY, float offsetSize)
        {
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
            newEntry.RequireContextKeys = this.RequireContextKeys;

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
                foreach (var i in this.Variants) if (i.IconVariantData != "" && i.IconVariantData.Length > 0 && i.Validate(tags) && i.Validate(Owner.Owner)) return i.IconVariantData;
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
            newEntry.RequireContextKeys = this.RequireContextKeys;
            newEntry.icon_path = this.icon_path;
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
                foreach (var i in this.Variants) if (i.Validate(tags) && i.Validate(Owner.Owner) && i.PortraitVariantData.Count > 0) return Utility.GetRandomElement(i.PortraitVariantData);
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
                if (portraitBox.spineRect != null) portraitBox.spineRect.gameObject.SetActive(false);
                portraitBox.picture.gameObject.SetActive(false);
                if (portraitBox.picture_landscape_group != null) portraitBox.picture_landscape_group.alpha = 1;
                portraitBox.currentPortrait = pathOverride;
                portraitBox.NotifyEndDraw();
            }
        }
    }
}

