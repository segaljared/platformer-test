using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class VectorExtensions
{
    public static Vector2 RotateDegrees(this Vector2 vector, float degrees)
    {
        return vector.RotateRadians(degrees * Mathf.Deg2Rad);
    }

    public static Vector2 RotateRadians(this Vector2 vector, float radians)
    {
        float sinRad = Mathf.Sin(radians);
        float cosRad = Mathf.Cos(radians);

        float rotatedX = vector.x * cosRad - vector.y * sinRad;
        float rotatedY = vector.x * sinRad + vector.y * cosRad;
        return new Vector2(rotatedX, rotatedY);
    }

    public static Vector2 CapMagnitude(this Vector2 vector, float cap)
    {
        float magnitude = vector.magnitude;
        if (magnitude > cap)
        {
            vector *= cap / magnitude;
        }
        return vector;
    }

    public static Vector3 SetXY(this Vector3 vector, Vector2 newXY)
    {
        vector.x = newXY.x;
        vector.y = newXY.y;
        return vector;
    }
}
