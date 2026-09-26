using UnityEngine;

/// <summary>
/// Physics layers used by the crew.
/// Crew        - each player's physics capsule (what actually moves and collides in gameplay).
/// CrewRagdoll - the floppy visual skeleton; purely cosmetic, ignored by all gameplay queries.
/// </summary>
public static class GameLayers
{
    public static readonly int Crew = LayerMask.NameToLayer("Crew");
    public static readonly int CrewRagdoll = LayerMask.NameToLayer("CrewRagdoll");

    /// <summary>Mask for gameplay raycasts/overlaps: everything except the cosmetic ragdolls.</summary>
    public static int Gameplay => CrewRagdoll >= 0 ? ~(1 << CrewRagdoll) : ~0;
}
