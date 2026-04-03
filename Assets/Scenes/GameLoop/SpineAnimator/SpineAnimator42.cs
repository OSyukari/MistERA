using Spine;
using Spine.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

public class SpineDataTiny_42 : SpineDataTiny
{
    public SpineAtlasAsset atlasAsset = null;
    public SkeletonBinary skeletonBinary = null;
    public AtlasAttachmentLoader attachmentLoader = null;
    public SkeletonData skeletonData = null;
    public AnimationStateData animationStateData = null;
    public SkeletonDataAsset skeletonDataAsset = null;
    public override string Version { get { return "4.2"; } }
    public override void Clear()
    {
        base.Clear();
        this.initialized = false;
        if (atlasAsset != null)
        {
            MonoBehaviour.Destroy(atlasAsset.atlasFile);
            MonoBehaviour.Destroy(atlasAsset);
            atlasAsset = null;
        }
        if (skeletonDataAsset != null)
        {
            skeletonDataAsset.Clear();
            MonoBehaviour.Destroy(skeletonDataAsset.skeletonJSON);
            MonoBehaviour.Destroy(skeletonDataAsset);
            skeletonDataAsset = null;
        }
    }
}

public class SpineLoaderTiny_42 : SpineLoaderTiny
{
    public SkeletonAnimation Animation = null;
    public override string Version { get { return "4.2"; } }

    public override void Clear()
    {
        if (Animation != null)
        {
            Animation.AnimationState.ClearTracks();
            Animation.ClearState();
            Animation.StopAllCoroutines();

            Animation.gameObject.SetActive(false);

            if (Animation.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
            {
                //Debug.Log("clear shared mesh");
                MonoBehaviour.DestroyImmediate(filter.sharedMesh);
            }
            Animation.skeletonDataAsset = null;

            MonoBehaviour.Destroy(Animation.gameObject);
            MonoBehaviour.Destroy(Animation);
            Animation = null;
        }
    }
    public override GameObject GameObject 
    { get
        {
            if (Animation == null) return null;
            return Animation.gameObject;
        }
    }
}

public class SpineAnimator42 : SpineAnimatorBase
{
    public override IEnumerator Initialize(PortraitManager.CharaPortrait_Spine manager, SpineLoaderTiny loader, List<string> texturePath, string atlasPath, string skeletonPath, bool straightAlpha, string idleAnimName = "idle", string addonAnimName = "action")
    {
        if (loader is SpineLoaderTiny_42) yield return Initialize(manager, loader as SpineLoaderTiny_42, texturePath, atlasPath, skeletonPath, straightAlpha, idleAnimName, addonAnimName);
        else
        {
            Debug.LogError("SpineAnimator42 Initialize called on wrong loader!");
            yield break;
        }
    }

    Coroutine co = null;

    public IEnumerator Initialize(PortraitManager.CharaPortrait_Spine manager, SpineLoaderTiny_42 loader, List<string> texturePath, string atlasPath, string skeletonPath, bool straightAlpha, string idleAnimName = "idle", string addonAnimName = "action")
    {
        bool refresh = false;

        SpineDataTiny_42 dataloader = null;

        if (manager.dataHolder == null)
        {
            dataloader = new SpineDataTiny_42();
            manager.dataHolder = dataloader;
        }
        else
        {
            dataloader = manager.dataHolder as SpineDataTiny_42;
        }

        if (loader.dataPointer != dataloader)
        {
            loader.dataPointer = dataloader;
            refresh = true;
            loader.Clear();
        }

        if (!dataloader.initialized || dataloader.atlasPath != atlasPath || dataloader.skeletonPath != skeletonPath || dataloader.texturePath != texturePath)
        {
            // SGmaterial.SetInt("_StraightAlphaInput", straightAlpha ? 1 : 0);
            loader.Clear();
            refresh = true;
            yield return PreCacheData(manager, texturePath, atlasPath, skeletonPath, straightAlpha);
        }

        if (refresh)
        {
            loader.Clear();
            //skeletonDataAsset.scale = skeletonScale;
            //Animation = SkeletonAnimation.AddToGameObject(this.gameObject, skeletonDataAsset);

            loader.Animation = SkeletonAnimation.NewSkeletonAnimationGameObject(dataloader.skeletonDataAsset);
            loader.Animation.Initialize(false);

            // Set default skin that has any mesh data to prevent Degenerate Triangle error.
            foreach (Skin skin in dataloader.skeletonData.Skins)
            {
                if (CheckSkinMesh(skin)) loader.Animation.Skeleton.SetSkin(skin.Name);
            }
            // Optional: play animation
            loader.Animation.Skeleton.SetToSetupPose();
        }


        if (loader.idleAnimName != idleAnimName)
        {
            refresh = true;
            var idleAnim = loader.Animation.skeletonDataAsset.GetSkeletonData(true).FindAnimation(idleAnimName);
            if (idleAnim == null)
            {
                var list = loader.Animation.skeletonDataAsset.GetSkeletonData(true).Animations.ToArray();
                idleAnim = list.Length > 0 ? list[0] : null;
                var names = new List<string>();
                foreach (var i in list) names.Add(i.Name);
                Debug.Log($"Spine animation name mismatch\nAtlasPath {atlasPath}\nValid Anims: {String.Join("|", names)}");
            }
            // send looping idle animation
            //self_SkeletonGraphic.AnimationState.AddAnimation(0, idleAnim, true, 0);
            if (idleAnim != null) loader.Animation.AnimationState.SetAnimation(0, idleAnim, true);
        }
        if (loader.addonAnimName != addonAnimName)
        {
            refresh = true;
            var addonAnim = loader.Animation.skeletonDataAsset.GetSkeletonData(true).FindAnimation(addonAnimName);
            if (addonAnim == null)
            {
                var list = loader.Animation.skeletonDataAsset.GetSkeletonData(true).Animations.ToArray();
                addonAnim = list.Length > 0 ? list[0] : null;
                var names = new List<string>();
                foreach (var i in list) names.Add(i.Name);
                Debug.Log($"Spine animation name mismatch\nAtlasPath {atlasPath}\nValid Anims: {String.Join("|", names)}");
            }
            // send looping idle animation
            //self_SkeletonGraphic.AnimationState.AddAnimation(0, idleAnim, true, 0);
            if (addonAnim != null) loader.Animation.AnimationState.SetAnimation(1, addonAnim, true);
        }

        if (refresh)
        {
            loader.Animation.Update(0);
            loader.Animation.LateUpdate();
        }
    }


    bool loading = false;

    public override IEnumerator PreCacheData(PortraitManager.CharaPortrait_Spine manager, List<string> texturePath, string atlasPath, string skeletonPath, bool straightAlpha)
    {
        if (loading) yield break;
        if (manager.dataHolder is SpineDataTiny_42 existing
            && existing.initialized
            && existing.atlasPath == atlasPath
            && existing.skeletonPath == skeletonPath 
            && existing.texturePath.SequenceEqual(texturePath))
            yield break;

        loading = true;

        SpineDataTiny_42 dataloader;
        if (manager.dataHolder is SpineDataTiny_42 reuse)
        {
            dataloader = reuse;
        }
        else
        {
            dataloader = new SpineDataTiny_42();
            manager.dataHolder = dataloader;
        }

        dataloader.Clear();
        dataloader.atlasPath = atlasPath;
        dataloader.texturePath = texturePath;

        if (dataloader.skeletonTA == null || dataloader.skeletonPath == "" || dataloader.skeletonPath != skeletonPath)
        {
            dataloader.skeletonPath = skeletonPath;
            yield return AssetsLoader.LoadSkelCoroutine(skeletonPath, text => dataloader.skeletonTA = text);
        }

        string atlasText = "";
        yield return AssetsLoader.LoadSkelCoroutine(scr_System_Serializer.current.GetFullPath(atlasPath), text => atlasText = Encoding.UTF8.GetString(text));

        dataloader.imageTextures = new Texture2D[texturePath.Count];
        int texDone = 0;
        for (int i = 0; i < texturePath.Count; i++)
        {
            int idx = i;
            Texture2D imageTexture = new(2, 2, TextureFormat.RGBA32, false);
            imageTexture.name = Path.GetFileNameWithoutExtension(texturePath[idx]);
            dataloader.imageTextures[idx] = imageTexture;
            StartCoroutine(AssetsLoader.LoadSkelCoroutine(texturePath[idx], bytes => { imageTexture.LoadImage(bytes); texDone++; }));
        }
        while (texDone < texturePath.Count) yield return null;

        dataloader.atlasAsset = SpineAtlasAsset.CreateRuntimeInstance(
            new TextAsset(atlasText),
            dataloader.imageTextures,
            straightAlpha ? this.SGmaterial_Alpha : this.SGmaterial,
            true);

        dataloader.attachmentLoader = new(dataloader.atlasAsset.GetAtlas());
        dataloader.skeletonBinary = new SkeletonBinary(dataloader.attachmentLoader);
        dataloader.skeletonData = dataloader.skeletonBinary.ReadSkeletonData(new MemoryStream(dataloader.skeletonTA));
        dataloader.skeletonBinary.Scale *= dataloader.spineScale;
        dataloader.animationStateData = new(dataloader.skeletonData);
        dataloader.skeletonDataAsset = CreateSkeletonDataAsset(dataloader.skeletonData, dataloader.animationStateData);
        dataloader.initialized = true;

        loading = false;
    }

    public static bool CheckSkinMesh(Skin skin)
    {
        foreach (Skin.SkinEntry entry in skin.Attachments)
        {
            if (entry.Attachment is MeshAttachment meshAttachment)
            {
                if (meshAttachment.Vertices.Length > 0 || meshAttachment.Triangles.Length > 0)
                    return true;
            }
        }
        return false;
    }
    public static SkeletonDataAsset CreateSkeletonDataAsset(SkeletonData skeletonData, AnimationStateData stateData)
    {

        // Create a new instance of SkeletonDataAsset
        SkeletonDataAsset skeletonDataAsset =
            ScriptableObject.CreateInstance<SkeletonDataAsset>();

        // Get the type of SkeletonDataAsset
        Type skeletonDataAssetType = skeletonDataAsset.GetType();

        // Get the skeletonData and stateData fields
        FieldInfo skeletonDataField = skeletonDataAssetType.GetField(
            "skeletonData",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        FieldInfo stateDataField = skeletonDataAssetType.GetField(
            "stateData",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        // Set the values of skeletonData and stateData
        skeletonDataField.SetValue(skeletonDataAsset, skeletonData);
        stateDataField.SetValue(skeletonDataAsset, stateData);

        // Set a dummy value to skeletonJSON variable to make sure there's no
        // error returned if we call some method in SkeletonDataAsset
        skeletonDataAsset.skeletonJSON = new TextAsset("NIKKE");
        // Return the SkeletonDataAsset
        return skeletonDataAsset;

    }
}
