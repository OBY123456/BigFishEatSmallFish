using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IMovement
{
    void Tick(Rigidbody2D rb, float currentSpeed);
    bool IsActive { get; set; }
}
