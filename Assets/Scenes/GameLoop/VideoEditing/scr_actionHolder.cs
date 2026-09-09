using System;
using UnityEngine;

public class scr_actionHolder : MonoBehaviour
{
    public RectTransform selfRect;
    public RectTransform innerObject;
    public RectTransform messageList;
    public scr_SelectableText toggleVisibility;

    public MessageCollect mcol;
    public CanvasGroup textCanvasGroup;

    public scr_HoverableText score, time;

    // filled post creation
    // paired evaluator-side holder for this AP, so this box's toggle can drive scoring
    public RecordingEvaluatorInstance.ActionHolder holder;

    // this data only lives in the editor ui
    bool active = true;

    public bool Activate
    {
        get
        {
            return active;
        }
        set
        {
            active = value;
            this.textCanvasGroup.alpha = active ? 1f : 0.5f;
            //this.titles.gameObject.SetActive(active);
            //innerObject.gameObject.SetActive(active);
        }
    }
}
