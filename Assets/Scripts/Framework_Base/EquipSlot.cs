using UnityEngine;


[System.Serializable]
public enum BodyEquipLayer
{
    None, Skin, Inner, Outer//, Shell
}

[System.Serializable]
public enum BodyPartEquipSlot
{
    None,
    // External
    Hand,
    Hair, Face, Eyes,
    Torso, Neck,
    Lower,
    Leg, Feet,
    // Internal
    Mouth,
    Stomach,
    Breasts, Nipples,
    Clitoris,
    Vagina,
    Womb,
    Urethra,
    Anus
}

[System.Serializable]
public enum Revealing
{
    Erotic = -1,
    SeeThrough = 0,
    ShapeReveal = 1,
    NonRevealing = 2,
    Armored = 3
}