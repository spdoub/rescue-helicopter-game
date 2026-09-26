using UnityEngine;

/// <summary>
/// Physics layers used by the crew.
/// Crew        - each player's physics capsule (what actually moves and collides in gameplay).
/// CrewRagdoll - the floppy visual skeleton; purely cosmetic, ignored by all gameplay queries.
/// CameraBlock - invisible copies of walls that only the first-person camera bumps into
///               (e.g. the helicopter's curved inner shell); collides with nothing.
/// </summary>
public static class GameLayers
{
    public static readonly int Crew = LayerMask.NameToLayer("Crew");
    public static readonly int CrewRagdoll = LayerMask.NameToLayer("CrewRagdoll");
    public static readonly int CameraBlock = LayerMask.NameToLayer("CameraBlock");

    static int Bit(int layer) => layer >= 0 ? 1 << layer : 0;

    /// <summary>Mask for gameplay raycasts/overlaps: everything except the cosmetic ragdolls and camera-only walls.</summary>
    public static int Gameplay => ~(Bit(CrewRagdoll) | Bit(CameraBlock));

    /// <summary>What the first-person camera must stay out of: gameplay walls plus camera-only walls, minus the crew.</summary>
    public static int CameraSolid => (Gameplay | Bit(CameraBlock)) & ~Bit(Crew);
}
