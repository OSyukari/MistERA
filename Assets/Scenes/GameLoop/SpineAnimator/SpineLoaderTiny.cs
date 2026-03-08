
using System.Collections.Generic;
using UnityEngine;

public abstract class SpineLoaderTiny
{
    public virtual string Version { get { return ""; } }

    /// <summary>
    /// Call External dispose code on SkeletonAnimation
    /// </summary>
    public virtual void Clear() 
    { 

    }

    public abstract GameObject GameObject { get; }
    public string idleAnimName = "";
    public string addonAnimName = "";
    public string touchAnimName = "";

    public SpineDataTiny dataPointer = null;

}
public abstract class SpineDataTiny
{
    public byte[] skeletonTA = null;
    public string skeletonPath = "";
    public string atlasPath = "";
    public List<string> texturePath = new List<string>();
    public virtual string Version { get { return ""; } }
    public virtual void Clear()
    {
        //Debug.Log("dataholder clear!");

        if (imageTextures != null)
        {
            for (int i = imageTextures.Length - 1; i >= 0; i--)
            {
                MonoBehaviour.DestroyImmediate(imageTextures[i]);
                imageTextures[i] = null;
            }
            imageTextures = null;
        }
    }

    //public Material spineLoader_Material;
    public float spineScale = 0.5f;
    public Texture2D[] imageTextures = null;

    public bool initialized = false;
}

