using Unity.Netcode;

/// <summary>
/// One tick of a crew member's controls, sent from their machine to the host.
/// </summary>
public struct PlayerInput : INetworkSerializable
{
    public float moveX;     // strafe  -1..1
    public float moveZ;     // forward -1..1
    public float yaw;       // world-space facing, degrees
    public float pitch;     // look up/down, degrees (+ is down)
    public bool jump;       // pressed since last send
    public bool sprint;     // held
    public bool grabLeft;   // left mouse held: left hand reaches / grabs
    public bool grabRight;  // right mouse held: right hand reaches / grabs
    public sbyte winch;     // at the winch box: +1 hold R (reel in), -1 hold Q (let out)

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref moveX);
        s.SerializeValue(ref moveZ);
        s.SerializeValue(ref yaw);
        s.SerializeValue(ref pitch);
        s.SerializeValue(ref jump);
        s.SerializeValue(ref sprint);
        s.SerializeValue(ref grabLeft);
        s.SerializeValue(ref grabRight);
        s.SerializeValue(ref winch);
    }
}
