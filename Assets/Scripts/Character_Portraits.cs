using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System.Linq;
using System;
using UnityEngine.UI;

[System.Serializable]
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

    public CharaPortrait GetValidPortrait()
    {

        if (portraitPriorityList == null || portraitPriorityList.Count == 0)
        {
            Debug.Log("GetValidPortrait return new");
            return new CharaPortrait_Image("", "");
        }

        foreach (var i in portraitPriorityList)
        {
            if (i.isValid())
            {
                //Debug.Log("GetValidPortrait list valid fo ownr "+Owner.FirstName);
                return i;
            }
        }
        //Debug.Log("GetValidPortrait return last");
        return portraitPriorityList[portraitPriorityList.Count - 1];
    }

    public void Click()
    {
        this.GetValidPortrait().Click();
    }

    [System.Serializable]
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

    [System.Serializable]
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

    [System.Serializable]
    public class CharaPortrait
    {
        [JsonIgnore] public PortraitManager Owner;

        public float portrait_offset_x;
        public float portrait_offset_y;
        public float portrait_offset_size;

        public float icon_offset_x;
        public float icon_offset_y;
        public float icon_offset_size;

        public bool Disable = false;

        public List<PortraitVariant> Variants = new List<PortraitVariant>();

        public virtual bool isValid() { return false; }
        public virtual IEnumerator DrawIcon(scr_CharIconBox iconBox)
        {
            iconBox.picture.sprite = SpriteAsset.transparent;
            yield break;
        }
        public virtual IEnumerator DrawPortrait(scr_CharPortraitBox portraitBox)
        {
            portraitBox.picture.gameObject.SetActive(true);
            if (portraitBox.spineRect != null) portraitBox.spineRect.gameObject.SetActive(false);
            //portraitBox.spineLoader.gameObject.SetActive(false);
            portraitBox.picture.sprite = SpriteAsset.transparent;
            yield break;
        }
        /*
        public virtual IEnumerator DrawPortrait(scr_Menu_CharaDetail movablePortraitBox)
        {
            //Debug.LogError("BaseDrawportrait claled");
            movablePortraitBox.picture.gameObject.SetActive(true);
            if (movablePortraitBox.spineRect != null) movablePortraitBox.spineRect.gameObject.SetActive(false);

            movablePortraitBox.picture.sprite = SpriteAsset.transparent;
            yield break;
        }*/

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

    [System.Serializable]
    public class CharaPortrait_Image : CharaPortrait
    {   // do not allow multiple transparent image layering -> should use spine instead.
        public string portrait_path = "";
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
        public override IEnumerator DrawIcon(scr_CharIconBox iconBox)
        {
            var drawImg = IconPath;
            if (drawImg == "") drawImg = PortraitPath;  // if no icon but have portrait then draw portrait using icon offset

            if (drawImg == "") yield return base.DrawIcon(iconBox);  // no fallback, draw transparent
            else
            {
                Texture2D loaded = null;
                yield return AssetsLoader.LoadTextureCoroutine(drawImg, texture => loaded = texture);

                iconBox.picture.sprite = scr_System_CentralControl.current.GetSprite(loaded);

                iconBox.picture.SetNativeSize();
                iconBox.picture.rectTransform.anchoredPosition = new Vector2(icon_offset_x, icon_offset_y);
                iconBox.picture.rectTransform.localScale = new Vector3(icon_offset_size, icon_offset_size, icon_offset_size);
            }
        }

        [JsonIgnore] public string IconPath
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

        [JsonIgnore] public string PortraitPath { get
            {
                if (this.Variants != null)
                {
                    foreach (var i in this.Variants) if (i.Validate(Owner.Owner) && i.PortraitVariantData != null && i.PortraitVariantData.Length > 0) return i.PortraitVariantData;
                }
                return portrait_path;
            } }

        public override IEnumerator DrawPortrait(scr_CharPortraitBox portraitBox)
        {

            //portraitBox.spineLoader.gameObject.SetActive(false);


            //Debug.Log("image drawportrait " + this.PortraitPath);
            if (this.PortraitPath == "")
            {
                yield return base.DrawPortrait(portraitBox);
            }
            else
            {
                Texture2D loaded = null;
                yield return AssetsLoader.LoadTextureCoroutine(PortraitPath, texture => loaded = texture);

                portraitBox.picture.gameObject.SetActive(true);
                if (portraitBox.spineRect != null) portraitBox.spineRect.gameObject.SetActive(false);

                var sprite = scr_System_CentralControl.current.GetSprite(loaded);

                portraitBox.picture.sprite = sprite;
                portraitBox.UpdateAnchor(this);
                //portraitBox.UpdateAnchor(this, portrait_offset_x, portrait_offset_y, portrait_offset_size);
            }
        }

        /*
        public override IEnumerator DrawPortrait(scr_Menu_CharaDetail movablePortraitBox)
        {        

            //movablePortraitBox.spineLoader.gameObject.SetActive(false);

            if (this.PortraitPath == "")
            {
                movablePortraitBox.picture.sprite = SpriteAsset.transparent;
            }
            else
            {
                Texture2D loaded = null;
                yield return AssetsLoader.LoadTextureCoroutine(PortraitPath, texture => loaded = texture);

                movablePortraitBox.picture.gameObject.SetActive(true);
                if (movablePortraitBox.spineRect != null) movablePortraitBox.spineRect.gameObject.SetActive(false);

                var sprite = scr_System_CentralControl.current.GetSprite(loaded);

                movablePortraitBox.picture.sprite = sprite;
                movablePortraitBox.picture.SetNativeSize();

                movablePortraitBox.picture.rectTransform.anchoredPosition = new Vector2(portrait_offset_x, portrait_offset_y );
                movablePortraitBox.picture.rectTransform.localScale = new Vector3(portrait_offset_size, portrait_offset_size, portrait_offset_size);

            }*
    }*/

    public override void SetPortraitOffsets(float offsetX, float offsetY, float offsetSize)
        {
            this.portrait_offset_x = offsetX;
            this.portrait_offset_y = offsetY;
            this.portrait_offset_size = offsetSize;
        }
    }


    [System.Serializable]
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
        public override IEnumerator DrawIcon(scr_CharIconBox iconBox)
        {
            if (IconPath == "") yield return base.DrawIcon(iconBox);
            else
            {
                Texture2D loaded = null;
                yield return AssetsLoader.LoadTextureCoroutine(IconPath, texture => loaded = texture);

                iconBox.picture.sprite = scr_System_CentralControl.current.GetSprite(loaded);

                iconBox.picture.SetNativeSize();
                iconBox.picture.rectTransform.anchoredPosition = new Vector2(icon_offset_x, icon_offset_y);
                iconBox.picture.rectTransform.localScale = new Vector3(icon_offset_size, icon_offset_size, icon_offset_size);
            }
        }

        [JsonIgnore]
        public string IconPath
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
        public string PortraitAnimName
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

        public override IEnumerator DrawPortrait(scr_CharPortraitBox portraitBox)
        {
            if (this.PortraitAnimName == "")
            {
                //portraitBox.spineLoader.gameObject.SetActive(false);
                yield return base.DrawPortrait(portraitBox);

            }
            else
            {
                //Debug.LogError($"PortraitAnimation name {PortraitAnimName}");
                //if (portraitBox.pictureRect == null) Debug.LogError("pictureRect null");

                //portraitBox.pictureRect.content = portraitBox.spineRect;
                //portraitBox.spineLoader.gameObject.SetActive(true);
                yield return portraitBox.spineLoader.SetBase(materialTexturePaths, atlasJSON_path, skeletonJSON_path, straightAlpha, PortraitAnimName );
                portraitBox.spineRect.gameObject.SetActive(true);
                portraitBox.picture.gameObject.SetActive(false);

                portraitBox.spineRect.SetParent(portraitBox.spineLoader.transform, false);
                portraitBox.UpdateAnchor(this);
                //portraitBox.spineLoader.spineLoader.MatchWithBound();
                // portraitBox.spineRect.anchoredPosition = new Vector2(portrait_offset_x, portrait_offset_y);
                // portraitBox.spineRect.localScale = new Vector3(portrait_offset_size, portrait_offset_size, portrait_offset_size);
                // portraitBox.picture.rectTransform.localScale = new Vector3(portrait_offset_size, portrait_offset_size, portrait_offset_size);
                // portraitBox.picture.rectTransform.localPosition = new Vector3(portrait_offset_x, portrait_offset_y, 0);

                // portraitBox.UpdateAnchor(this, portrait_offset_x, portrait_offset_y, portrait_offset_size);
            }
        }

        /*
        public override IEnumerator DrawPortrait(scr_Menu_CharaDetail movablePortraitBox)
        {
            if (this.PortraitAnimName == "")
            {
                //Debug.LogError("PortraitAnimatio name null disabling");
                movablePortraitBox.picture.gameObject.SetActive(true);
                //movablePortraitBox.spineLoader.gameObject.SetActive(false);

                movablePortraitBox.picture.sprite = SpriteAsset.transparent;
                yield break;
            }
            else
            {
                //Debug.LogError($"PortraitAnimation name {PortraitAnimName}");

                yield return movablePortraitBox.spineLoader.SetBase(materialTexturePaths, atlasJSON_path, skeletonJSON_path, straightAlpha, PortraitAnimName);
                movablePortraitBox.pictureRect.content = movablePortraitBox.spineRect;
                movablePortraitBox.spineRect.gameObject.SetActive(true);
                movablePortraitBox.picture.gameObject.SetActive(false);

                Debug.Log($"Drawportrait is null {movablePortraitBox.spineRect == null} {movablePortraitBox.spineLoader == null} ");
                movablePortraitBox.spineRect.SetParent(movablePortraitBox.spineLoader.transform, false);
              //  movablePortraitBox.spineLoader.spineLoader.MatchWithBound();
               // movablePortraitBox.spineRect.anchoredPosition = new Vector2(portrait_offset_x , portrait_offset_y );
               // movablePortraitBox.spineRect.localScale = new Vector3(portrait_offset_size, portrait_offset_size, portrait_offset_size);

                movablePortraitBox.spineRect.localScale = new Vector3(portrait_offset_size, portrait_offset_size, portrait_offset_size);
                movablePortraitBox.spineRect.localPosition = new Vector3(portrait_offset_x, portrait_offset_y, 0);

            }

        }*/

        public override void SetPortraitOffsets(float offsetX, float offsetY, float offsetSize)
        {
            //Debug.Log("SetPortraitOffsets [" + offsetX + "] ["+offsetY+"] ["+offsetSize+"]");
            this.portrait_offset_x = offsetX;
            this.portrait_offset_y = offsetY;
            this.portrait_offset_size = offsetSize;
        }
    }
}

