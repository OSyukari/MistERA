using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class initScript_Records : MonoBehaviour
{
    public RectTransform AbnormalExpGrid;
    public scr_HoverableText viewEXPBTN;

    public List<labelGrid> managedGrids = new List<labelGrid>();
    public labelGrid unlabeled;

    Dictionary<string, labelGrid> labeled = new Dictionary<string, labelGrid>();

    public RectTransform GetGrid(List<string> label)
    {
        foreach(var l in label)
        {
            if (labeled.ContainsKey(l))
            {
                labeled[l].NotifyInsert();
                return labeled[l].selfRect;
            }
        }
        unlabeled.NotifyInsert();
        return unlabeled.selfRect;
    }

    public void Initialize(Character_Trainable c)
    {

        foreach (var i in managedGrids)
        {
            labeled.Add(i.gridLabel, i);
            i.Clear();
        }

        unlabeled.Clear();
    }

}
