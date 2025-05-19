using UnityEngine;

public class Character_Gender_base
{
    public string ID;
    public int defaultB, defaultP, defaultV, defaultA;
}

public class Character_Gender_Male : Character_Gender_base
{
    public new string ID = "charGender_male";
    public new int defaultB = 0, defaultP = 1, defaultV = 0, defaultA = 1;
}

public class Character_Gender_Female : Character_Gender_base
{
    public new string ID = "charGender_female";
    public new int defaultB = 1, defaultP = 0, defaultV = 1, defaultA = 1;
}

public class Character_Gender_Ambiguous : Character_Gender_base
{
    public new string ID = "charGender_ambiguous";
    public new int defaultB = 0, defaultP = 0, defaultV = 0, defaultA = 0;
}