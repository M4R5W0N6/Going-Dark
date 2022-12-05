using UnityEngine;

public class PlayerData : MonoBehaviour
{
    public static PlayerData LocalPlayer
    {
        get
        {
            return FindObjectOfType<PlayerData>(); // temp
        }
    }

    public Vector3 Position; // where the player currently is
    public Vector3 Rotation; // how much the player is rotated (origin.pitch, character.yaw)
    public Vector3 Direction; // where the player is looking (screenTarget - originPosition)
    public Vector3 OriginPosition;  // where the player's raycast is determined from
    public bool IsOnTarget; // whether the player is going to shoot where they're aiming
    public Vector3 TargetPosition; // where the player is aiming (in worldspace)
    public Vector3 RaycastPosition; // where the player is going to shoot (in worldspace)
}
