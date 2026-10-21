using UnityEngine;

public class FishAI : MonoBehaviour
{
    public float speed = 3f;
    public float turnSpeed = 5f;
    
    // 由生成器传进来的活动范围
    public Rect roamBounds; 

    private Vector3 _targetPos;

    private void Start()
    {
        PickNewTarget();
    }

    private void Update()
    {
        Vector3 direction = _targetPos - transform.position;

        // 如果距离目标点很近了，就重新找个新位置
        if (direction.sqrMagnitude < 0.5f)
        {
            PickNewTarget();
        }
        else
        {
            // 向目标点移动
            transform.position = Vector3.MoveTowards(transform.position, _targetPos, speed * Time.deltaTime);
            
            // 平滑转向目标点
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            Quaternion targetRotation = Quaternion.AngleAxis(angle, Vector3.forward);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        }
    }

    private void PickNewTarget()
    {
        // 在矩形范围内随机取一个点
        float randomX = Random.Range(roamBounds.xMin, roamBounds.xMax);
        float randomY = Random.Range(roamBounds.yMin, roamBounds.yMax);
        _targetPos = new Vector3(randomX, randomY, transform.position.z);
    }
}