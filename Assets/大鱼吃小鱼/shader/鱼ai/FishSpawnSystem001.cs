using System.Collections;
using System.Collections.Generic; 
using UnityEngine;

// 定义一个配置类，用来在面板上设置不同种类的鱼
[System.Serializable]
public class FishSpawnConfig
{
    public string configName = "普通小鱼";
    public GameObject fishPrefab;     // 鱼的预制体
    public int spawnCount = 10;       // 生成数量
    public float minSpeed = 2f;       // 最小速度
    public float maxSpeed = 4f;       // 最大速度
    public float initialWeight = 5f;  // 初始体重
}

// 主类名设定为 FishSpawnSystem001
public class FishSpawnSystem001 : MonoBehaviour
{
    [Header("生成区域设置")]
    public Vector2 spawnAreaSize = new Vector2(30f, 20f);

    [Header("鱼群种类配置")]
    // 避开尖括号的数组写法
    public FishSpawnConfig[] fishConfigs; 

    private void Start()
    {
        // 计算出具体的矩形范围
        Rect area = new Rect(
            transform.position.x - spawnAreaSize.x / 2,
            transform.position.y - spawnAreaSize.y / 2,
            spawnAreaSize.x, 
            spawnAreaSize.y
        );

        // 遍历所有配置并生成鱼
        if (fishConfigs == null) return;

        foreach (var config in fishConfigs)
        {
            for (int i = 0; i < config.spawnCount; i++)
            {
                // 【核心修改】：将写死的 0f 改为 transform.position.z，让鱼跟随生成器的 Z 轴深度
                Vector3 spawnPos = new Vector3(
                    Random.Range(area.xMin, area.xMax),
                    Random.Range(area.yMin, area.yMax),
                    transform.position.z 
                );

                // 生成实体
                if (config.fishPrefab == null) continue;
                GameObject fishGo = Instantiate(config.fishPrefab, spawnPos, Quaternion.identity);

                // 1. 设置 FishEntity 的基础属性 
                FishEntity entity = fishGo.GetComponent(typeof(FishEntity)) as FishEntity;
                if (entity != null)
                {
                    entity.isPlayer = false; 
                    entity.initialWeight = config.initialWeight; 
                }

                // 2. 动态添加 AI 脚本并传递参数
                FishAI ai = fishGo.AddComponent(typeof(FishAI)) as FishAI;
                if (ai != null)
                {
                    ai.speed = Random.Range(config.minSpeed, config.maxSpeed); 
                    ai.roamBounds = area; 
                }
            }
        }
    }

    // 在 Scene 窗口画一个绿色的框，方便你直观地调整区域大小
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        // 让画出来的绿框也能完全匹配当前的 Z 轴深度
        Gizmos.DrawWireCube(transform.position, new Vector3(spawnAreaSize.x, spawnAreaSize.y, 0f));
    }
}