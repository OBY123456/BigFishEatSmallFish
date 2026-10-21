using UnityEngine;

public class FishMovement : MonoBehaviour
{
    public float speed = 5f;
    public float turnSpeed = 10f;
    private Camera _cam;

    private void Start()
    {
        _cam = Camera.main;
        if (_cam == null)
        {
            Debug.LogError("【严重错误】找不到主摄像机！请选中场景中的 Camera，把它的 Tag 设置为 'MainCamera'！");
        }
    }

    private void Update()
    {
        if (_cam == null) return;

        // 1. 从摄像机向鼠标在屏幕上的位置发射一条 3D 射线
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        
        // 2. 创建一个虚拟的 3D 平面 (以鱼当前的 Z 轴深度为基准，面朝摄像机)
        Plane zPlane = new Plane(Vector3.back, new Vector3(0, 0, transform.position.z));

        // 3. 计算射线打在平面上的交点
        if (zPlane.Raycast(ray, out float hitDistance))
        {
            Vector3 worldPos = ray.GetPoint(hitDistance); // 鼠标在游戏世界里的精准坐标
            Vector2 direction = (worldPos - transform.position);

            // 当距离鼠标超过一点点距离时才移动，防止鱼在鼠标指针处疯狂抽搐
            if (direction.sqrMagnitude > 0.05f)
            {
                // 移动
                transform.position = Vector3.MoveTowards(transform.position, worldPos, speed * Time.deltaTime);
                
                // 转向：计算鼠标与鱼的夹角，并平滑旋转过去
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                Quaternion targetRotation = Quaternion.AngleAxis(angle, Vector3.forward);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
            }
        }
    }
}