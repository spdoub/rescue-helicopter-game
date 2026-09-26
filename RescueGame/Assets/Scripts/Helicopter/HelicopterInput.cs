using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// One frame of pilot controls, all in the range -1..1. Small enough to send over the network every tick.
/// </summary>
public struct HelicopterInput : INetworkSerializable
{
    public float collective; // up / down
    public float pitch;      // nose down (forward) / nose up (back)
    public float roll;       // lean right / left
    public float yaw;        // turn right / left

    public static readonly HelicopterInput None = default;

    /// <summary>Keyboard: Space/Shift up/down, W/S forward/back, A/D sideways, Q/E turn. Gamepad: sticks + triggers.</summary>
    public static HelicopterInput ReadDevices()
    {
        var i = new HelicopterInput();

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.spaceKey.isPressed) i.collective += 1f;
            if (kb.leftShiftKey.isPressed) i.collective -= 1f;
            if (kb.wKey.isPressed) i.pitch += 1f;
            if (kb.sKey.isPressed) i.pitch -= 1f;
            if (kb.dKey.isPressed) i.roll += 1f;
            if (kb.aKey.isPressed) i.roll -= 1f;
            if (kb.eKey.isPressed) i.yaw += 1f;
            if (kb.qKey.isPressed) i.yaw -= 1f;
        }

        var pad = Gamepad.current;
        if (pad != null)
        {
            Vector2 left = pad.leftStick.ReadValue();
            i.pitch += left.y;
            i.roll += left.x;
            i.yaw += pad.rightStick.ReadValue().x;
            i.collective += pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
        }

        i.collective = Mathf.Clamp(i.collective, -1f, 1f);
        i.pitch = Mathf.Clamp(i.pitch, -1f, 1f);
        i.roll = Mathf.Clamp(i.roll, -1f, 1f);
        i.yaw = Mathf.Clamp(i.yaw, -1f, 1f);
        return i;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref collective);
        serializer.SerializeValue(ref pitch);
        serializer.SerializeValue(ref roll);
        serializer.SerializeValue(ref yaw);
    }
}
