using UnityEngine;
using Newtonsoft.Json;

public enum BodyEquipLayer
{
    None, Skin, Inner, Outer//, Shell
}

public enum BodyPartEquipSlot
{
    None,
    // External
    Hand, Arm,
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
    Anus,
    // External, added later: appended so existing int values stay stable
    Shoulder, Thigh
}

public enum Revealing
{
    Erotic = -1,
    SeeThrough = 0,
    ShapeReveal = 1,
    NonRevealing = 2,
    Armored = 3
}