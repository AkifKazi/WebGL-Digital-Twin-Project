using UnityEngine;

public class DualRotorSpin : MonoBehaviour
{
    public Transform rotor01;
    public Transform rotor02;

    public Vector3 rotor01Axis = Vector3.forward;
    public Vector3 rotor02Axis = Vector3.forward;

    public float rpm = 1200f;
    public bool oppositeDirections = true;

    void Update()
    {
        float degreesPerSecond = rpm * 6f * Time.deltaTime;

        if (rotor01 != null)
            rotor01.Rotate(rotor01Axis.normalized, degreesPerSecond, Space.Self);

        if (rotor02 != null)
        {
            float direction = oppositeDirections ? -1f : 1f;
            rotor02.Rotate(rotor02Axis.normalized, degreesPerSecond * direction, Space.Self);
        }
    }
}