using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;


public class PortraitManager
{
    public List<CharaPortrait> portraitPriorityList = null;

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
    }

    public void Prepend(CharaPortrait cm)
    {
        if (this.portraitPriorityList == null || this.portraitPriorityList.Count < 1) this.portraitPriorityList = new List<CharaPortrait>();
        this.portraitPriorityList.Insert(0,cm);
        cm.Owner = this;

        Debug.Log("new portrait, owner ["+Owner.FirstName+"] cmOwner["+cm.Owner.Owner.FirstName+"] isImage[" + (cm is PortraitManager.CharaPortrait_Image) + "] listCount["+portraitPriorityList.Count+"] variantsCount [" + cm.Variants.Count + "] isvalid[" + (cm.isValid()) + "] portraitPath[" + (cm as PortraitManager.CharaPortrait_Image).PortraitPath + "]");
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
    protected void GetValidPortrait(List<string> keywords, out CharaPortrait handler, out string portrait, out string icon)
    {
        handler = null;
        if (portraitPriorityList == null || portraitPriorityList.Count == 0)
        {
            handler = Transparent;
            portrait = "";
            icon = "";
        }
        else
        {
            foreach (var i in portraitPriorityList)
            {
                if (!i.isValid()) continue;
                if (keywords.Count < 1 && i.RequireContextKeys.Count > 0) continue;
                if (!Utility.ListContainsStrict(keywords, i.RequireContextKeys))
                {
                    Debug.Log($"portrait prioriry missing [{i.RequireContextKeys}] from [{String.Join(" ", keywords)}]");
                    continue;
                }
                else
                {

                }
                handler = i;
                break;
            }
            if (handler == null)
            {
                handler = portraitPriorityList[portraitPriorityList.Count - 1];
            }

            portrait = handler.PortraitPath;
            icon = handler.IconPath != "" ? handler.IconPath : portrait;
        }
    }

    public void ClearHandlerCache()
    {
        _cache_NeutralPortrait = null;
        _cache_CombatPortrait = null;
        _cache_ActivityPortrait = null;
    }


    [JsonIgnore] public CharaPortrait _cache_NeutralPortrait = null;
    protected string _cache_NeutralPortrait_path = "";
    protected string _cache_NeutralPortrait_icon = "";
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
            box.currentPortrait = _cache_NeutralPortrait_path;
            scr_System_CampaignManager.current.CurrentTargetEXPortrait = box.currentHandler;
            box.Draw(_cache_NeutralPortrait.DrawPortrait(box, _cache_NeutralPortrait_path));
        }
    }
    [JsonIgnore] public CharaPortrait _cache_CombatPortrait = null;
    protected string _cache_CombatPortrait_path = "";
    protected string _cache_CombatPortrait_icon = "";
    public void DrawCombatPortrait(I_StatsManager stats, scr_CharPortraitBox box, bool forceRefresh = false)
    {
        if (_cache_CombatPortrait == null || forceRefresh)
        {
            GetValidPortrait(new List<string>() { "combat" }, out _cache_CombatPortrait, out _cache_CombatPortrait_path, out _cache_CombatPortrait_icon);
            tags_combat = new List<string>() { "combat" };
        }
        if (box.currentHandler != _cache_CombatPortrait || box.currentPortrait != _cache_CombatPortrait_path)
        {
            box.currentHandler = _cache_CombatPortrait;
            box.currentPortrait = _cache_CombatPortrait_path;
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
    [JsonIgnore] public CharaPortrait _cache_ActivityPortrait = null;
    protected string _cache_ActivityPortrait_path = "";
    protected string _cache_ActivityPortrait_icon = "";
    protected void DrawActivityPortrait(scr_CharPortraitBox box, List<string> tags = null)
    {
        //Debug.Log($"{Owner.CallName} DrawActivityPortrait");
        if (_cache_ActivityPortrait == null || (tags != null && tags.Count > 0))
        {
            if (tags == null || tags.Count < 1) tags = GetOwnerActionTagsByPriority();
            Debug.Log($"{Owner.CallName} DrawActivityPortrait Tags {String.Join(" ", tags)}");
            GetValidPortrait(tags, out _cache_ActivityPortrait, out _cache_ActivityPortrait_path, out _cache_ActivityPortrait_icon);
            tags_active = tags;
        }
        if (box.currentHandler != _cache_ActivityPortrait || box.currentPortrait != _cache_ActivityPortrait_path)
        {
            Debug.Log($"{Owner.CallName} DrawActivityPortrait with path {_cache_ActivityPortrait_path}");
            box.currentHandler = _cache_ActivityPortrait;
            box.currentPortrait = _cache_ActivityPortrait_path;
            box.Draw(_cache_ActivityPortrait.DrawPortrait(box, _cache_ActivityPortrait_path));
        }

    }

    protected List<string> GetOwnerActionTagsByPriority()
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
            Debug.Log($"{Owner.CallName} drawActivityIcon Tags {String.Join(" ", tags)}");
            GetValidPortrait(tags, out _cache_ActivityPortrait, out _cache_ActivityPortrait_path, out _cache_ActivityPortrait_icon);
            tags_active = tags;
        }
        if (box.currentHandler != _cache_ActivityPortrait || box.currentIcon != _cache_ActivityPortrait_icon)
        {
            Debug.Log($"{Owner.CallName} drawActivityIcon with path {_cache_ActivityPortrait_path}");
            box.currentHandler = _cache_ActivityPortrait;
            box.currentIcon = _cache_ActivityPortrait_icon;
            box.Draw(_cache_ActivityPortrait.DrawIcon(box, _cache_ActivityPortrait_icon));
        }

    }
    public void DrawPortrait(scr_CharPortraitBox box, List<string> tags = null)
    {
        if (box.isCurrentTargetEXBox) DrawNeutralPortrait(box);
        else if (box.isCurrentTargetBox) DrawActivityPortrait(box, tags);
        else
        {
            box.currentPortrait = null;
        }
    }
    public void DrawIcon(scr_CharIconBox box)
    {
        drawActivityIcon(box);
    }
    public void ActivityClick()
    {
        if (_cache_ActivityPortrait == null)
        {
            var tags = GetOwnerActionTagsByPriority();
            GetValidPortrait(GetOwnerActionTagsByPriority(), out _cache_ActivityPortrait, out _cache_ActivityPortrait_path, out _cache_ActivityPortrait_icon);
            tags_active = tags;
        }
        _cache_ActivityPortrait.Click();
        Debug.Log("clicked!");
    }

    public class PortraitVariant
    {
        public List<PortraitValidators> Conditions = new List<PortraitValidators>();
        public string PortraitVariantData = "";
        public string IconVariantData = "";
        public bool Validate(Character_Trainable c)
        {
            if (Conditions == null || Conditions.Count < 1) return true;
            foreach(var i in Conditions) if (!i.Validate(c)) return false;

            return true;
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


        [JsonIgnore] public virtual string IconPath { get; }

        [JsonIgnore] public virtual string PortraitPath { get; }

        public List<string> RequireContextKeys = new List<string>();
        public virtual bool isValid() { return false; }
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
        public virtual IEnumerator DrawPortrait(scr_CharPortraitBox portraitBox, string pathOverride)
        {
            Debug.Log($"null drawportrait [{pathOverride}]");
            portraitBox.picture.gameObject.SetActive(true);
            if (portraitBox.spineRect != null) portraitBox.spineRect.gameObject.SetActive(false);
            //portraitBox.spineLoader.gameObject.SetActive(false);
            portraitBox.picture.sprite = SpriteAsset.transparent;
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
    }

    public class CharaPortrait_Image : CharaPortrait
    {   // do not allow multiple transparent image layering -> should use spine instead.
        public string portrait_path = "";
        public List<string> random_portrait_path = new List<string>();
        public string icon_path="";

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
                Texture2D loaded = null;
                yield return AssetsLoader.LoadTextureCoroutine(pathOverride, texture => loaded = texture);

                iconBox.picture.sprite = scr_System_CentralControl.current.GetSprite(loaded);

                iconBox.picture.SetNativeSize();
                iconBox.picture.rectTransform.anchoredPosition = new Vector2(icon_offset_x, icon_offset_y);
                iconBox.picture.rectTransform.localScale = new Vector3(icon_offset_size, icon_offset_size, icon_offset_size);
            }
        }

        [JsonIgnore] public override string IconPath
        {
            get
            {
                if (this.Variants != null)
                {
                    foreach (var i in this.Variants) if (i.Validate(Owner.Owner) && i.IconVariantData != null && i.IconVariantData.Length > 0) return i.IconVariantData;
                }
                return icon_path;
            }
        }

        [JsonIgnore] public override string PortraitPath { get
            {
                if (this.Variants != null)
                {
                    foreach (var i in this.Variants) if (i.Validate(Owner.Owner) && i.PortraitVariantData != null && i.PortraitVariantData.Length > 0) return i.PortraitVariantData;
                }
                if (this.random_portrait_path.Count > 0) return Utility.GetRandomElement(this.random_portrait_path);
                else return portrait_path;
            } }

        public override IEnumerator DrawPortrait(scr_CharPortraitBox portraitBox, string pathOverride)
        {
            if (pathOverride == "")
            {
                yield return base.DrawPortrait(portraitBox, pathOverride);
            }
            else
            {
                Debug.Log($"image drawportrait [{pathOverride}]");
                Texture2D loaded = null;
                yield return AssetsLoader.LoadTextureCoroutine(pathOverride, texture => loaded = texture);

                portraitBox.picture.gameObject.SetActive(true);
                if (portraitBox.spineRect != null) portraitBox.spineRect.gameObject.SetActive(false);

                var sprite = scr_System_CentralControl.current.GetSprite(loaded);

                portraitBox.picture.sprite = sprite;
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

        public string icon_path = "";
        public bool straightAlpha = false;
        public CharaPortrait_Spine()
        {

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

        public override bool isValid() { return !Disable; }
        public override IEnumerator DrawIcon(scr_CharIconBox iconBox, string pathOverride )
        {
            if (pathOverride == "") yield return base.DrawIcon(iconBox, pathOverride);
            else
            {
                Texture2D loaded = null;
                yield return AssetsLoader.LoadTextureCoroutine(pathOverride, texture => loaded = texture);

                iconBox.picture.sprite = scr_System_CentralControl.current.GetSprite(loaded);

                iconBox.picture.SetNativeSize();
                iconBox.picture.rectTransform.anchoredPosition = new Vector2(icon_offset_x, icon_offset_y);
                iconBox.picture.rectTransform.localScale = new Vector3(icon_offset_size, icon_offset_size, icon_offset_size);
            }
        }

        [JsonIgnore]
        public override string IconPath
        {
            get
            {
                if (this.Variants != null)
                {
                    foreach (var i in this.Variants) if (i.Validate(Owner.Owner) && i.IconVariantData != null && i.IconVariantData.Length > 0) return i.IconVariantData;
                }
                return icon_path;
            }
        }

        [JsonIgnore]
        public override string PortraitPath
        {
            get
            {
                if (this.Variants != null)
                {
                    foreach (var i in this.Variants) if (i.Validate(Owner.Owner) && i.PortraitVariantData != null && i.PortraitVariantData.Length > 0) return i.PortraitVariantData;
                }
                return idleAnimName;
            }
        }

        /// <summary>
        /// Spine variant
        /// </summary>
        /// <param name="portraitBox"></param>
        /// <param name="pathOverride">pathOverride is its animation variant name</param>
        /// <returns></returns>
        public override IEnumerator DrawPortrait(scr_CharPortraitBox portraitBox, string pathOverride)
        {
            if (pathOverride == "")
            {
                //portraitBox.spineLoader.gameObject.SetActive(false);
                yield return base.DrawPortrait(portraitBox, pathOverride);

            }
            else
            {
               // Debug.Log($"spine drawportrait [{pathOverride}]");
                //Debug.LogError($"PortraitAnimation name {PortraitAnimName}");
                yield return portraitBox.spineLoader.SetBase(materialTexturePaths, atlasJSON_path, skeletonJSON_path, straightAlpha, pathOverride);
                portraitBox.spineRect.gameObject.SetActive(true);
                portraitBox.picture.gameObject.SetActive(false);

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
}

