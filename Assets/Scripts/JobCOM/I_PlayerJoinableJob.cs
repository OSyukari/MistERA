/// <summary>
/// A specially tracked job (I_RequireSpecialTracker) the player may join without being its actor - e.g. a C-section
/// whose doctor's operation package waits for the player patient. While OffersJoinTo is true the command panel tracks
/// the job and shows its joinable packages (Job.JoinablePackages) as join buttons.
/// </summary>
public interface I_PlayerJoinableJob
{
    bool OffersJoinTo(Character_Trainable player);
}
