using System.Collections;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// 对象池教学模版（Resources + Queue）。正式业务请使用 <see cref="PrefabPoolManager"/> + <see cref="PrefabPool"/>。
/// GetObj：队列有则 Dequeue 并激活；无则 Instantiate。RecycleObj：Deactivate 后 Enqueue。
/// </summary>
public class PoolTemp
{
    private static PoolTemp _instance;

    private Dictionary<string, Queue<GameObject>> pool;//对象池
    private Dictionary<string,GameObject> prefabs;//预设体
    
    /// <summary>
    /// 构造函数
    /// </summary>
    private PoolTemp()
    {
        pool = new Dictionary<string, Queue<GameObject>>();
        prefabs = new Dictionary<string, GameObject>();
    }

    /// <summary>
    /// 获取单例
    /// </summary>
    /// <returns></returns>
    public static PoolTemp GetInstance()
    {
        if (_instance == null)
        {
            _instance = new PoolTemp();
        }
        return _instance;
    }

    /// <summary>
    /// 从对象池中获取对象
    /// </summary>
    /// <param name="objName"></param>
    /// <returns></returns>
    public GameObject GetObj(string objType)
    {
        //结果对象
        GameObject result = null;
        //判断是否有该名字的对象池
        if (pool.ContainsKey(objType))
        {
            //对象池里有对象
            if (pool[objType].Count > 0)
            {
                //获取结果
                result = pool[objType].Dequeue();
                //激活对象
                result.SetActive(true);
                //返回结果
                return result;
            }
        }

        //如果没有该对象的对象池或者该对象池没有对象
        GameObject prefab = null;
        //如果已经加载过该预设体
        if (prefabs.ContainsKey(objType)){
            prefab = prefabs[objType];
        }
        else//如果没有加载过该预设体
        {
            //Debug.Log("Prefabs/" + objType.ToString());
            prefab = Resources.Load<GameObject>("Prefabs/" + objType.ToString());
            //更新字典
            prefabs.Add(objType, prefab);
        }

        //生成
        result = GameObject.Instantiate(prefab);
        return result;
    }

    /// <summary>
    /// 回收对象到对象池
    /// </summary>
    /// <param name="obj"></param>
    public void RecycleObj(GameObject obj,string objType)
    {
        //设置为非激活
        obj.SetActive(false);
        //判断是否有该对象的对象池
        if (pool.ContainsKey(objType))
        {
            //放置到该对象池
            pool[objType].Enqueue(obj);
        }
        else
        {
            //创建该类型的池，将对象放入
            Queue<GameObject> temp = new Queue<GameObject>();
            temp.Enqueue(obj);
            pool.Add(objType, temp);
        }
    }

}
