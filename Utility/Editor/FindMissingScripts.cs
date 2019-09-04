using UnityEngine;
using UnityEditor;

/// <summary>
/// 作用：查找指定预制体上丢失的脚本
/// 
/// 描述：这是一个编辑器脚本，需要放到Editor目录下。
/// 写这类脚本固定有3步,
/// 1、继承EditorWindow，表示这是一个扩展编辑器窗口
/// 2、入口方法上添加MenuItem属性，这里是ShowWindow方法
/// 3、在入口方法里调用EditorWindow.GetWindow，作用是从菜单弹出这个编辑器窗口
/// 4、添加OnGUI方法，这里绘制了一个Button，执行查找
/// </summary>
public class FindMissingScripts : EditorWindow
{
    [MenuItem("Window/FindMissingScripts")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow(typeof(FindMissingScripts));
    }

    public void OnGUI()
    {
        if (GUILayout.Button("Find Missing Scripts in selected prefabs"))
        {
            FindInSelected();
        }
    }
    private static void FindInSelected()
    {
        GameObject[] go = Selection.gameObjects;    //选择的资源，这里是预制体
        int go_count = 0, components_count = 0, missing_count = 0;  //作统计：游戏对象数量、组件数量、丢失数量
        foreach (GameObject g in go)
        {
            go_count++;
            Component[] components = g.GetComponents<Component>();  //获取所有组件
            for (int i = 0; i < components.Length; i++)
            {
                components_count++;
                if (components[i] == null)
                {
                    missing_count++;
                    string s = g.name;
                    Transform t = g.transform;
                    while (t.parent != null)   //如果这个对象有父物体，输出的时候，带上父物体的名字  
                    {
                        s = t.parent.name + "/" + s;
                        t = t.parent;
                    }
                    Debug.Log(s + " has an empty script attached in position: " + i, g);
                }
            }
        }

        Debug.Log(string.Format("Searched {0} GameObjects, {1} components, found {2} missing", go_count, components_count, missing_count));
    }
}
