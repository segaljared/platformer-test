using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    public Transform ToFollow;

    // Update is called once per frame
    void Update()
    {
        transform.position = transform.position.SetXY(ToFollow.position);
    }
}
