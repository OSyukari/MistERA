using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class scr_memoryDaySplit : MonoBehaviour
{
    public TMP_Text text;
    public Image selfImage;
    public RectTransform selfRect;
    private void Awake()
    {
        selfImage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;
    }
}
