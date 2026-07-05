using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;


public static class ReproductionUtility
{

    public static string defaultWombPath = "RJW - Womb/Womb.png";

    public static Dictionary<MenstruationStatus, string> MenstruationStatus_Override = new Dictionary<MenstruationStatus, string>()
    {
        { MenstruationStatus.Menstrual, "RJW - Womb/Womb_Bleeding.png" }
    };

    public static Dictionary<EstrusStatus, string> EstrusStatus_Override = new Dictionary<EstrusStatus, string>();

    public static string[] cumOverlays = new string[]
    {
        "RJW - Womb/Womb_Cum_00.png",
        "RJW - Womb/Womb_Cum_01.png",
        "RJW - Womb/Womb_Cum_02.png",
        "RJW - Womb/Womb_Cum_03.png",
        "RJW - Womb/Womb_Cum_04.png",
        "RJW - Womb/Womb_Cum_05.png",
        "RJW - Womb/Womb_Cum_06.png",
        "RJW - Womb/Womb_Cum_07.png",
        "RJW - Womb/Womb_Cum_08.png",
        "RJW - Womb/Womb_Cum_09.png",
        "RJW - Womb/Womb_Cum_10.png",
        "RJW - Womb/Womb_Cum_11.png",
        "RJW - Womb/Womb_Cum_12.png",
        "RJW - Womb/Womb_Cum_13.png",
        "RJW - Womb/Womb_Cum_14.png",
        "RJW - Womb/Womb_Cum_15.png",
        "RJW - Womb/Womb_Cum_16.png",
        "RJW - Womb/Womb_Cum_17.png"
    };

    public static List<string> fertilizingStages = new List<string>()
    {
        "RJW - Ovulation/Egg_Fertilizing00.png",
        "RJW - Ovulation/Egg_Fertilizing01.png",
        "RJW - Ovulation/Egg_Fertilizing02.png",
    };

    public static string[] fertilizedStages = new string[]
    {
        "RJW - Ovulation/Egg_Fertilized00.png",
        "RJW - Ovulation/Egg_Fertilized01.png",
        "RJW - Ovulation/Egg_Fertilized02.png"
    };

    public static string[] releaseStages = new string[]
    {
        "RJW - Ovulation/Ovary_01.png",
        "RJW - Ovulation/Ovary_02.png"
    };
    public static string egg_implanted = "RJW - Ovulation/Egg_Implanted00.png";
    public static string egg_active = "RJW - Ovulation/Egg.png";

    public static string ovary_active = "RJW - Ovulation/Ovary_00.png";
}

