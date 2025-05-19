using UnityEngine;
using System.IO;
using Spine.Unity;
using System.Linq;
using System.Collections.Generic;


public class SpineLoader_42 : SpineLoader
{
    public override string Version { get { return "4.2"; } }
    public SkeletonDataAsset prefab_spineLoader_SDA;
    public SpineAtlasAsset prefab_spineLoader_SAA;

    protected SpineAtlasAsset spineLoader_SAA;
    protected SkeletonDataAsset spineLoader_SDA = null;

    public SkeletonGraphic self_SkeletonGraphic;
    public Material SGmaterial;

    protected override void Awake()
    {
        spineLoader_Material = Instantiate(prefab_spineLoader_Material);

        spineLoader_SAA = Instantiate(prefab_spineLoader_SAA);
        spineLoader_SAA.materials = new Material[1];
        spineLoader_SAA.materials[0] = spineLoader_Material;

        spineLoader_SDA = Instantiate(prefab_spineLoader_SDA);
        spineLoader_SDA.atlasAssets = new AtlasAssetBase[1];
        spineLoader_SDA.atlasAssets[0] = spineLoader_SAA;
        foreach (AtlasAssetBase aa in spineLoader_SDA.atlasAssets) if (aa != null) aa.Clear();
    }

    public override void Initialize(string folderPath, List<string> texturePath, string atlasPath, string skeletonPath, float skeletonScale, string idleAnimName = "idle", string touchAnimName = "action")
    {
        if (this.atlasPath == atlasPath && this.skelPath == skeletonPath && this.gameObject.activeInHierarchy) return;

        resourceFolderPath = folderPath;
        DefaultAnimation = idleAnimName;
        TouchAnimation = touchAnimName;
        skelPath = skeletonPath;
        this.atlasPath = atlasPath;
        this.texturesPath = texturePath;

        spineLoader_atlasJSON = scr_System_CentralControl.current.LoadResourcesTextAssets(atlasPath);
        spineLoader_skeletonJSON = scr_System_CentralControl.current.LoadResourcesTextAssets(skeletonPath);

        spineLoader_Texture = new Texture2D[texturePath.Count];

        spineLoader_SAA.materials = new Material[texturePath.Count];
        for (int i = 0; i < texturePath.Count; i++)
        {
            spineLoader_Texture[i] = scr_System_CentralControl.current.LoadCachedTexture(texturePath[i]);
            spineLoader_Texture[i].name = Path.GetFileNameWithoutExtension(texturePath[i]);

            var m = Instantiate(prefab_spineLoader_Material);
            m.mainTexture = spineLoader_Texture[i];
            spineLoader_SAA.materials[i] = m;
        }




        //spineLoader_Material.mainTexture = spineLoader_Texture[0];
        spineLoader_SAA.atlasFile = spineLoader_atlasJSON;

        foreach (AtlasAssetBase aa in spineLoader_SDA.atlasAssets) if (aa != null) aa.Clear();
        spineLoader_SDA.Clear();
        spineLoader_SDA.skeletonJSON = spineLoader_skeletonJSON;
        spineLoader_SDA.scale = skeletonScale;
        spineLoader_SDA.GetSkeletonData(true);

        self_SkeletonGraphic.skeletonDataAsset = spineLoader_SDA;
        self_SkeletonGraphic.material = SGmaterial;

        this.gameObject.SetActive(true);
        self_SkeletonGraphic.SetMaterialDirty();
        self_SkeletonGraphic.Initialize(true);
        self_SkeletonGraphic.LateUpdate();

        var idleAnim = self_SkeletonGraphic.skeletonDataAsset.GetSkeletonData(true).FindAnimation(idleAnimName);
        if (idleAnim != null)
        {// send looping idle animation
            //self_SkeletonGraphic.AnimationState.AddAnimation(0, idleAnim, true, 0);
            self_SkeletonGraphic.AnimationState.SetAnimation(0, idleAnim, true);
            self_SkeletonGraphic.startingAnimation = idleAnimName;
        }

    }

    public override void MatchWithBound()
    {
        self_SkeletonGraphic.MatchRectTransformWithBounds();
    }

}