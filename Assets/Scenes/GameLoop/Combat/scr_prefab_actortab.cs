using UnityEngine;

public class scr_prefab_actortab : MonoBehaviour
{
    public scr_HoverableText name, hp, mp, status, action;
    public scr_SelectableText btn_plus, btn_minus;

    public RectTransform actionList;
    public RectTransform SelfRect;

    public void Load(Character_Trainable c)
    {
        this.name.SetText(c.FirstName);
        this.hp.SetText(c.Stats.HP != null ? $"{c.Stats.HP.Value}/{c.Stats.HP.MaxValue}" : "-/-");
        this.mp.SetText(c.Stats.MP != null ? $"{c.Stats.MP.Value}/{c.Stats.MP.MaxValue}" : "-/-");
    }
}
