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
    Anus
}

public enum Revealing
{
    Erotic = -1,
    SeeThrough = 0,
    ShapeReveal = 1,
    NonRevealing = 2,
    Armored = 3
}