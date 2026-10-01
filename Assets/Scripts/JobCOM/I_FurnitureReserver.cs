/// <summary>
/// A job that reserves a piece of furniture (Job_Furniture.ReserveFor) while it is active, e.g. a medical job holding the
/// patient's bed. Only characters it allows may use that furniture meanwhile (the player is never blocked).
/// </summary>
public interface I_FurnitureReserver
{
    bool AllowsFurnitureUse(Character_Trainable c);
}
